using System.Net;
using System.Text.Json;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

public sealed record SteamWebApiProbeResult(bool IsSuccess, string Message, int GameCount = 0);

public sealed class SteamWebApiProbeService
{
    private const string Endpoint =
        "https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/";
    private readonly Func<NetworkSettings?, HttpClient> _clientFactory;

    public SteamWebApiProbeService(Func<NetworkSettings?, HttpClient>? clientFactory = null)
    {
        _clientFactory = clientFactory ?? NetworkHttpClientFactory.Create;
    }

    public async Task<SteamWebApiProbeResult> TestAsync(
        SteamLibrarySettings rawSettings,
        NetworkSettings networkSettings,
        CancellationToken cancellationToken = default)
    {
        SteamLibrarySettings settings;
        try
        {
            settings = SteamLibrarySettingsValidator.RequireConfigured(rawSettings);
        }
        catch (ArgumentException exception)
        {
            return new SteamWebApiProbeResult(false, exception.Message);
        }

        var query =
            $"key={Uri.EscapeDataString(settings.ApiKey)}" +
            $"&steamid={Uri.EscapeDataString(settings.SteamId)}" +
            "&include_appinfo=false&include_played_free_games=true&format=json";
        using var client = _clientFactory(networkSettings);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?{query}");
        request.Headers.UserAgent.ParseAdd("SATLI/SteamLibraryTest");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new SteamWebApiProbeResult(
                    false,
                    "Steam 拒绝了凭据，请检查 API Key 和 SteamID64。");
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: timeout.Token);
            if (!document.RootElement.TryGetProperty("response", out var payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return new SteamWebApiProbeResult(false, "Steam 返回了无效的游戏库响应。");
            }

            var count = payload.TryGetProperty("game_count", out var gameCount)
                && gameCount.TryGetInt32(out var parsedCount)
                    ? parsedCount
                    : payload.TryGetProperty("games", out var games)
                        && games.ValueKind == JsonValueKind.Array
                            ? games.GetArrayLength()
                            : 0;
            return new SteamWebApiProbeResult(
                true,
                $"连接成功：Steam 返回 {count} 个游戏。若数量偏少，请检查游戏详情隐私设置。",
                count);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            or OperationCanceledException
            or JsonException)
        {
            var message = exception is JsonException
                ? "Steam 返回了无法解析的游戏库数据。"
                : NetworkErrorMessage.Describe(exception, "测试 Steam 游戏库连接");
            return new SteamWebApiProbeResult(false, message);
        }
    }
}
