using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed class GitHubIntegrationService
{
    public const string Repository = "GaBoron/steam-achievement-translation-library";
    private readonly GitHubCredentialStore _store;
    private readonly Func<HttpClient> _clientFactory;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public GitHubIntegrationService(
        Func<NetworkSettings>? networkSettings = null,
        GitHubCredentialStore? store = null,
        Func<HttpClient>? clientFactory = null)
    {
        _store = store ?? new GitHubCredentialStore();
        _clientFactory = clientFactory
            ?? (() => NetworkHttpClientFactory.Create(networkSettings?.Invoke()));
    }

    public string ClientId =>
        Environment.GetEnvironmentVariable("SATL_GITHUB_CLIENT_ID")?.Trim()
        is { Length: > 0 } configured
            ? configured
            : Assembly.GetEntryAssembly()?
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "GitHubClientId")
                ?.Value?.Trim() ?? string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    public async Task<GitHubAccount?> GetAccountAsync()
    {
        var credential = await _store.LoadAsync();
        return credential is null ? null : new GitHubAccount(credential.Login, credential.AvatarUrl);
    }

    public async Task<GitHubDeviceChallenge> StartDeviceFlowAsync(
        CancellationToken cancellationToken = default)
    {
        RequireConfigured();
        using var client = CreateClient();
        using var response = await client.PostAsync(
            "https://github.com/login/device/code",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = ClientId }),
            cancellationToken);
        var payload = await ReadJsonAsync(response, cancellationToken);
        EnsureSuccess(response, payload, "请求 GitHub 设备码");
        var expiresIn = payload.GetProperty("expires_in").GetInt32();
        return new GitHubDeviceChallenge(
            payload.GetProperty("device_code").GetString()
                ?? throw new InvalidDataException("GitHub 未返回 device_code。"),
            payload.GetProperty("user_code").GetString()
                ?? throw new InvalidDataException("GitHub 未返回 user_code。"),
            new Uri(payload.GetProperty("verification_uri").GetString()
                ?? "https://github.com/login/device"),
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            TimeSpan.FromSeconds(payload.GetProperty("interval").GetInt32()));
    }

    public async Task<GitHubAccount> CompleteDeviceFlowAsync(
        GitHubDeviceChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        RequireConfigured();
        var interval = challenge.PollInterval;
        while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
        {
            await Task.Delay(interval, cancellationToken);
            using var client = CreateClient();
            using var response = await client.PostAsync(
                "https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = challenge.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }),
                cancellationToken);
            var payload = await ReadJsonAsync(response, cancellationToken);
            if (payload.TryGetProperty("error", out var errorElement))
            {
                var error = errorElement.GetString();
                if (error == "authorization_pending")
                {
                    continue;
                }
                if (error == "slow_down")
                {
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                }
                throw new InvalidOperationException(error switch
                {
                    "access_denied" => "GitHub 授权已被取消。",
                    "expired_token" => "GitHub 设备码已过期，请重新绑定。",
                    "device_flow_disabled" => "GitHub App 尚未启用 Device Flow。",
                    _ => $"GitHub 授权失败：{error}",
                });
            }
            EnsureSuccess(response, payload, "完成 GitHub 授权");
            var token = ParseToken(payload);
            var account = await ReadUserAsync(token.AccessToken, cancellationToken);
            await _store.SaveAsync(token with
            {
                Login = account.Login,
                AvatarUrl = account.AvatarUrl,
            });
            return account;
        }
        throw new InvalidOperationException("GitHub 设备码已过期，请重新绑定。");
    }

    public async Task<Uri> CreateReportIssueAsync(
        GitHubReportDraft draft,
        CancellationToken cancellationToken = default)
    {
        GitHubReportFormatter.Validate(draft);
        var token = await GetValidAccessTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("尚未绑定 GitHub 账户。");
        var (status, payload) = await SendIssueAsync(token, draft, cancellationToken);
        if (status == HttpStatusCode.Unauthorized)
        {
            token = await GetValidAccessTokenAsync(cancellationToken, forceRefresh: true)
                ?? throw new InvalidOperationException("GitHub 授权已失效，请重新绑定。");
            (status, payload) = await SendIssueAsync(token, draft, cancellationToken);
            if (status == HttpStatusCode.Unauthorized)
            {
                _store.Clear();
                throw new InvalidOperationException("GitHub 授权已失效，请重新绑定。");
            }
        }
        EnsureSuccess(status, payload, "创建 GitHub 报告");
        return new Uri(payload.GetProperty("html_url").GetString()
            ?? throw new InvalidDataException("GitHub 未返回 Issue 地址。"));
    }

    public void Unbind() => _store.Clear();

    private async Task<string?> GetValidAccessTokenAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var credential = await _store.LoadAsync();
            if (credential is null)
            {
                return null;
            }
            if (!forceRefresh
                && credential.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return credential.AccessToken;
            }
            if (string.IsNullOrWhiteSpace(credential.RefreshToken)
                || credential.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow)
            {
                _store.Clear();
                return null;
            }
            using var client = CreateClient();
            using var response = await client.PostAsync(
                "https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = credential.RefreshToken,
                }),
                cancellationToken);
            var payload = await ReadJsonAsync(response, cancellationToken);
            EnsureSuccess(response, payload, "刷新 GitHub 授权");
            var refreshed = ParseToken(payload) with
            {
                Login = credential.Login,
                AvatarUrl = credential.AvatarUrl,
            };
            await _store.SaveAsync(refreshed);
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<(HttpStatusCode Status, JsonElement Payload)> SendIssueAsync(
        string token,
        GitHubReportDraft draft,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(token);
        using var response = await client.PostAsync(
            $"https://api.github.com/repos/{Repository}/issues",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    title = GitHubReportFormatter.Title(draft),
                    body = GitHubReportFormatter.Body(draft),
                }),
                Encoding.UTF8,
                "application/json"),
            cancellationToken);
        return (response.StatusCode, await ReadJsonAsync(response, cancellationToken));
    }

    private async Task<GitHubAccount> ReadUserAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(accessToken);
        using var response = await client.GetAsync("https://api.github.com/user", cancellationToken);
        var payload = await ReadJsonAsync(response, cancellationToken);
        EnsureSuccess(response, payload, "读取 GitHub 账户");
        return new GitHubAccount(
            payload.GetProperty("login").GetString()
                ?? throw new InvalidDataException("GitHub 未返回登录名。"),
            payload.TryGetProperty("avatar_url", out var avatar)
                ? avatar.GetString() ?? string.Empty
                : string.Empty);
    }

    private HttpClient CreateClient(string? accessToken = null)
    {
        var client = _clientFactory();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"SATLInstaller/{UpdateService.CurrentVersionText}");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }
        return client;
    }

    private static GitHubCredential ParseToken(JsonElement payload)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresIn = payload.TryGetProperty("expires_in", out var expires)
            ? expires.GetInt32()
            : 8 * 60 * 60;
        var refreshExpiresIn = payload.TryGetProperty("refresh_token_expires_in", out var refreshExpires)
            ? refreshExpires.GetInt32()
            : 180 * 24 * 60 * 60;
        return new GitHubCredential(
            string.Empty,
            string.Empty,
            payload.GetProperty("access_token").GetString()
                ?? throw new InvalidDataException("GitHub 未返回 access_token。"),
            now.AddSeconds(expiresIn),
            payload.TryGetProperty("refresh_token", out var refresh)
                ? refresh.GetString() ?? string.Empty
                : string.Empty,
            now.AddSeconds(refreshExpiresIn));
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text)
                .RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("GitHub 返回了无效 JSON。", exception);
        }
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        JsonElement payload,
        string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var message = payload.TryGetProperty("message", out var value)
            ? value.GetString()
            : null;
        throw new HttpRequestException(
            $"{operation}失败：{message ?? response.ReasonPhrase}",
            null,
            response.StatusCode);
    }

    private static void EnsureSuccess(
        HttpStatusCode status,
        JsonElement payload,
        string operation)
    {
        if ((int)status is >= 200 and < 300)
        {
            return;
        }
        var message = payload.TryGetProperty("message", out var value)
            ? value.GetString()
            : null;
        throw new HttpRequestException(
            $"{operation}失败：{message ?? status.ToString()}",
            null,
            status);
    }

    private void RequireConfigured()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException(
                "GitHub App Client ID 尚未配置。请注册并安装 GitHub App 后设置 SATL_GITHUB_CLIENT_ID。");
        }
    }
}
