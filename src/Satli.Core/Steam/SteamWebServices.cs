using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.Models;

namespace Satli.Core.Steam;

public static class SteamWebServices
{
    private const int MaximumResponseBytes = 32 * 1024 * 1024;

    public static async Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(
        HttpClient client,
        string apiKey,
        string steamId,
        CancellationToken cancellationToken = default)
    {
        apiKey = apiKey.Trim();
        steamId = steamId.Trim();
        if (apiKey.Length != 32 || apiKey.Any(character => !Uri.IsHexDigit(character)))
            throw new PreflightException("Steam Web API Key 应为 32 位十六进制字符");
        if (steamId.Length != 17 || !steamId.All(char.IsAsciiDigit))
            throw new PreflightException("SteamID64 应为 17 位数字");
        var uri = new Uri(
            "https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/"
            + $"?key={Uri.EscapeDataString(apiKey)}&steamid={steamId}"
            + "&include_appinfo=true&include_played_free_games=true&format=json");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("SATLI/2.4.0");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new PreflightException("Steam Web API 拒绝了凭据，请检查 API Key 和 SteamID64");
        if ((int)response.StatusCode == 429)
            throw new PreflightException("Steam Web API 请求过于频繁，请稍后重试");
        response.EnsureSuccessStatusCode();
        var payload = await ReadLimitedAsync(response, MaximumResponseBytes, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("response", out var root)
            || !root.TryGetProperty("games", out var games)
            || games.ValueKind != JsonValueKind.Array)
            return [];
        var result = new Dictionary<string, OwnedGame>(StringComparer.Ordinal);
        foreach (var game in games.EnumerateArray())
        {
            if (!game.TryGetProperty("appid", out var rawId)
                || !rawId.TryGetInt64(out var number)
                || number <= 0)
                continue;
            var id = number.ToString();
            var name = game.TryGetProperty("name", out var rawName)
                ? rawName.GetString()?.Trim() ?? ""
                : "";
            result[id] = new OwnedGame(id, name);
        }
        return result.Values.OrderBy(game => ulong.Parse(game.AppId)).ToArray();
    }

    public static void MergeOwnedGames(
        IDictionary<string, DiscoveryRecord> records,
        IEnumerable<OwnedGame> games,
        string steamId)
    {
        foreach (var game in games)
        {
            if (!records.TryGetValue(game.AppId, out var record))
            {
                record = new DiscoveryRecord(game.AppId, game.Name);
                records[game.AppId] = record;
            }
            if (record.GameName.Length == 0 && game.Name.Length > 0)
                record.GameName = game.Name;
            record.Discovery.Add("steam-web-api");
            record.Accounts.Add(steamId);
        }
    }

    public static async Task<IReadOnlyDictionary<string, string>> ResolveNamesAsync(
        HttpClient client,
        string dataDirectory,
        IEnumerable<string> appIds,
        Action<int, int, string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var cachePath = Path.Combine(dataDirectory, "cache", "steam-game-names.json");
        var cache = LoadNameCache(cachePath);
        var requested = appIds.Distinct().ToArray();
        var result = requested.Where(cache.ContainsKey)
            .ToDictionary(id => id, id => cache[id], StringComparer.Ordinal);
        var pending = requested.Where(id => !result.ContainsKey(id)).ToArray();
        var completed = 0;
        using var semaphore = new SemaphoreSlim(4);
        var tasks = pending.Select(async id =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var name = await ResolveNameAsync(client, id, cancellationToken);
                lock (result)
                {
                    if (name.Length > 0)
                    {
                        result[id] = name;
                        cache[id] = name;
                    }
                    completed++;
                    progress?.Invoke(completed, pending.Length, id);
                }
            }
            catch
            {
                lock (result)
                {
                    completed++;
                    progress?.Invoke(completed, pending.Length, id);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();
        await Task.WhenAll(tasks);
        if (cache.Count > 0) SaveNameCache(cachePath, cache);
        return result;
    }

    private static async Task<string> ResolveNameAsync(
        HttpClient client,
        string appId,
        CancellationToken cancellationToken)
    {
        foreach (var uri in new[]
        {
            new Uri($"https://store.steampowered.com/api/appdetails?appids={appId}&l=schinese&cc=CN"),
            new Uri($"https://api.steamcmd.net/v1/info/{appId}"),
        })
        {
            try
            {
                using var response = await client.GetAsync(uri, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;
                var payload = await ReadLimitedAsync(response, 2 * 1024 * 1024, cancellationToken);
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (uri.Host == "store.steampowered.com")
                {
                    if (root.TryGetProperty(appId, out var item)
                        && item.TryGetProperty("success", out var success)
                        && success.GetBoolean()
                        && item.TryGetProperty("data", out var data)
                        && data.TryGetProperty("name", out var name))
                        return name.GetString()?.Trim() ?? "";
                }
                else if (root.TryGetProperty("data", out var data)
                    && data.TryGetProperty(appId, out var item)
                    && item.TryGetProperty("common", out var common)
                    && common.TryGetProperty("name", out var name))
                    return name.GetString()?.Trim() ?? "";
            }
            catch (Exception exception) when (
                exception is HttpRequestException or JsonException or TaskCanceledException)
            {
            }
        }
        return "";
    }

    private static Dictionary<string, string> LoadNameCache(string path)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root?["version"]?.GetValue<int>() != 1
                || root["names"] is not JsonObject names)
                return new Dictionary<string, string>(StringComparer.Ordinal);
            return names.Where(pair => pair.Key.All(char.IsAsciiDigit)
                    && pair.Value is JsonValue)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value!.GetValue<string>(),
                    StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void SaveNameCache(string path, Dictionary<string, string> names)
    {
        var values = new JsonObject();
        foreach (var pair in names.OrderBy(pair => ulong.Parse(pair.Key)))
            values[pair.Key] = pair.Value;
        var root = new JsonObject { ["version"] = 1, ["names"] = values };
        FileOperations.WriteDurable(
            path,
            System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }) + "\n"));
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new PreflightException("联网响应超过安全上限");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > maximumBytes)
                throw new PreflightException("联网响应超过安全上限");
            output.Write(buffer, 0, count);
        }
    }
}
