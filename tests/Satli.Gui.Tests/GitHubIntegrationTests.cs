using System.Net;
using System.Text;
using System.Text.Json;
using Satli_Gui.Models;
using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class GitHubIntegrationTests
{
    [Fact]
    public void ReportFormatterMatchesTranslationLibraryTemplate()
    {
        var draft = Draft();
        var body = GitHubReportFormatter.Body(draft);

        Assert.Equal("[文件错误] Test Game (123)", GitHubReportFormatter.Title(draft));
        Assert.Contains("### 游戏名", body);
        Assert.Contains("### Steam app ID", body);
        Assert.Contains("### 错误类型", body);
        Assert.Contains("文件可能过期", body);
        Assert.Contains("### 错误说明", body);
        Assert.Contains("### 参考来源", body);
    }

    [Fact]
    public async Task CredentialStoreEncryptsTokensAtRestAndCanUnbind()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"satli-github-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "github-auth.json");
        try
        {
            var store = new GitHubCredentialStore(path);
            var credential = new GitHubCredential(
                "octocat",
                "https://avatars.example/octocat",
                "ghu_access-secret",
                DateTimeOffset.UtcNow.AddHours(8),
                "ghr_refresh-secret",
                DateTimeOffset.UtcNow.AddMonths(6));

            await store.SaveAsync(credential);
            var serialized = await File.ReadAllTextAsync(path);
            var loaded = await store.LoadAsync();

            Assert.DoesNotContain("ghu_access-secret", serialized);
            Assert.DoesNotContain("ghr_refresh-secret", serialized);
            Assert.Equal(credential, loaded);
            store.Clear();
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task CreateReportUsesBoundUserAndDoesNotRequestLabels()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"satli-github-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "github-auth.json");
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        try
        {
            var store = new GitHubCredentialStore(path);
            await store.SaveAsync(new GitHubCredential(
                "octocat",
                "",
                "ghu_test",
                DateTimeOffset.UtcNow.AddHours(1),
                "ghr_test",
                DateTimeOffset.UtcNow.AddMonths(1)));
            var service = new GitHubIntegrationService(
                store: store,
                clientFactory: () => new HttpClient(new StubHandler(async request =>
                {
                    captured = request;
                    capturedBody = await request.Content!.ReadAsStringAsync();
                    return JsonResponse(
                        HttpStatusCode.Created,
                        """{"html_url":"https://github.com/GaBoron/steam-achievement-translation-library/issues/99"}""");
                })));

            var uri = await service.CreateReportIssueAsync(Draft());

            Assert.Equal("https://github.com/GaBoron/steam-achievement-translation-library/issues/99", uri.ToString());
            Assert.Equal("Bearer", captured?.Headers.Authorization?.Scheme);
            Assert.Equal("ghu_test", captured?.Headers.Authorization?.Parameter);
            Assert.DoesNotContain("\"labels\"", capturedBody);
            Assert.Contains("### Steam app ID", capturedBody);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task DeviceFlowHandlesPendingAndSlowDownThenStoresAccount()
    {
        var previous = Environment.GetEnvironmentVariable("SATLI_GITHUB_CLIENT_ID");
        var directory = Path.Combine(Path.GetTempPath(), $"satli-github-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "github-auth.json");
        var pollCount = 0;
        try
        {
            Environment.SetEnvironmentVariable("SATLI_GITHUB_CLIENT_ID", "Iv1.test-client");
            var service = new GitHubIntegrationService(
                store: new GitHubCredentialStore(path),
                clientFactory: () => new HttpClient(new StubHandler(request =>
                {
                    if (request.RequestUri!.AbsolutePath == "/login/device/code")
                    {
                        return Task.FromResult(JsonResponse(
                            HttpStatusCode.OK,
                            """{"device_code":"device","user_code":"ABCD-EFGH","verification_uri":"https://github.com/login/device","expires_in":60,"interval":0}"""));
                    }
                    if (request.RequestUri.AbsolutePath == "/login/oauth/access_token")
                    {
                        pollCount++;
                        return Task.FromResult(pollCount switch
                        {
                            1 => JsonResponse(HttpStatusCode.OK, """{"error":"authorization_pending"}"""),
                            2 => JsonResponse(HttpStatusCode.OK, """{"error":"slow_down"}"""),
                            _ => JsonResponse(
                                HttpStatusCode.OK,
                                """{"access_token":"ghu_device","expires_in":28800,"refresh_token":"ghr_device","refresh_token_expires_in":15897600}"""),
                        });
                    }
                    return Task.FromResult(JsonResponse(
                        HttpStatusCode.OK,
                        """{"login":"octocat","avatar_url":"https://avatars.example/octocat"}"""));
                })));

            var challenge = await service.StartDeviceFlowAsync();
            var account = await service.CompleteDeviceFlowAsync(challenge);

            Assert.Equal("ABCD-EFGH", challenge.UserCode);
            Assert.Equal("octocat", account.Login);
            Assert.Equal(3, pollCount);
            Assert.Equal("octocat", (await service.GetAccountAsync())?.Login);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SATLI_GITHUB_CLIENT_ID", previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static GitHubReportDraft Draft() => new(
        "Test Game",
        "123",
        "https://store.steampowered.com/app/123/",
        "文件可能过期",
        "游戏更新后新增了成就。",
        "https://store.steampowered.com/news/app/123/");

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request);
    }
}
