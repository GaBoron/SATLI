using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Satli.Core.Formats;

namespace Satli.Core.TranslationProjects;

public sealed record PublicAchievementSource(JsonObject Translation, string GameName, string Source, DateTimeOffset RetrievedAt,
    IReadOnlyList<string>? Warnings = null);

/// <summary>Reads Steam's public structured page. Missing identities never become guessed achievements.</summary>
public static partial class SteamPublicAchievements
{
    public static async Task<PublicAchievementSource> FetchManyAsync(HttpClient client, string appId,
        IReadOnlyList<string> languages, string variantId, CancellationToken token)
    {
        if (languages.Count is < 1 or > 4 || languages.Distinct(StringComparer.Ordinal).Count() != languages.Count)
            throw new UsageException("请输入 1–4 个不重复的原文语言代码，以逗号分隔。");
        var primary = await FetchAsync(client, appId, languages[0], variantId, token);
        using var gate = new SemaphoreSlim(2);
        var pending = languages.Skip(1).Select(async language =>
        {
            await gate.WaitAsync(token);
            try { return (Language: language, Source: await FetchAsync(client, appId, language, variantId, token)); }
            catch (Exception exception) when (!token.IsCancellationRequested && exception is HttpRequestException
                or TaskCanceledException or System.Text.Json.JsonException or SatliException or InvalidOperationException)
            { return (Language: language, Source: (PublicAchievementSource?)null); }
            finally { gate.Release(); }
        }).ToArray();
        var warnings = new List<string>();
        var sources = new List<string> { primary.Source };
        var original = primary.Translation.DeepClone().AsObject();
        var achievements = original["achievements"]!.AsObject();
        foreach (var extra in await Task.WhenAll(pending))
        {
            var values = extra.Source?.Translation["achievements"]?.AsObject();
            if (values is null || !achievements.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(values.Select(pair => pair.Key)))
            {
                warnings.Add($"{extra.Language} 未取得与首种语言一致的可信 API ID 集合，未合入工程。");
                continue;
            }
            foreach (var pair in achievements)
                pair.Value!["translations"]![extra.Language] = values[pair.Key]!["translations"]![extra.Language]!.DeepClone();
            sources.Add(extra.Source!.Source);
        }
        var available = achievements.First().Value!["translations"]!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal);
        original["languages"] = new JsonArray(available.Select(language => (JsonNode)JsonValue.Create(language)!).ToArray());
        TranslationJson.Parse(TranslationJsonExport.Serialize(original), appId, allowIncomplete: true);
        return new(original, primary.GameName, string.Join("\n", sources), primary.RetrievedAt, warnings);
    }

    public static async Task<PublicAchievementSource> FetchAsync(HttpClient client, string appId,
        string language, string variantId, CancellationToken cancellationToken)
    {
        if (!uint.TryParse(appId, out var id) || id == 0 || id.ToString() != appId
            || !Regex.IsMatch(language, @"\A[a-z][a-z0-9_]{1,31}\z"))
            throw new UsageException("App ID 或原文语言无效。");
        var url = $"https://steamcommunity.com/stats/{appId}/achievements/?l={language}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("SATLI");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = Encoding.UTF8.GetString(await ReadLimitedAsync(response, 8 * 1024 * 1024, cancellationToken));
        var translation = Parse(html, appId, language, variantId);
        var gameName = $"Steam 游戏 {appId}";
        try
        {
            using var details = await client.GetAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l={language}",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (details.IsSuccessStatusCode)
            {
                var data = JsonNode.Parse(await ReadLimitedAsync(details, 2 * 1024 * 1024, cancellationToken));
                gameName = data?[appId]?["data"]?["name"]?.GetValue<string>() ?? gameName;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            or InvalidOperationException or TaskCanceledException && !cancellationToken.IsCancellationRequested) { }
        return new(translation, gameName, url, DateTimeOffset.UtcNow);
    }

    public static JsonObject Parse(string html, string appId, string language, string variantId = "default")
    {
        var match = StructuredScript().Match(html);
        if (!match.Success) throw new PreflightException("Steam 未提供结构化成就数据，请导入可信 JSON；不会推测隐藏成就。");
        var root = JsonNode.Parse(match.Groups[1].Value)!.AsObject();
        var found = new List<JsonObject>();
        void Visit(JsonNode? node)
        {
            if (node is JsonObject map)
            {
                if (map["appid"]?.ToString() == appId && map["language"]?.ToString() == language && map["groups"] is JsonArray)
                    found.Add(map);
                else foreach (var pair in map) Visit(pair.Value);
            }
            else if (node is JsonArray array) foreach (var item in array) Visit(item);
        }
        JsonNode? Expand(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text)
            ? JsonNode.Parse(text) : node;
        if (root["loaderData"] is JsonArray loaders) foreach (var item in loaders) Visit(Expand(item));
        Visit(Expand(root["renderContext"]?["queryData"]));
        if (found.Count != 1) throw new PreflightException("公开数据的 App ID、语言或来源不明确，请补充可信 JSON。");
        var rows = new List<AchievementPreviewRow>();
        foreach (var group in found[0]["groups"]!.AsArray())
        {
            if (group?["achievements"] is not JsonArray achievements)
                throw new PreflightException("公开成就分组不完整。");
            foreach (var item in achievements)
            {
                var apiName = item?["api_name"]?.GetValue<string>();
                var name = item?["name"]?.GetValue<string>();
                var description = item?["description"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(apiName) || string.IsNullOrWhiteSpace(name) || description is null)
                    throw new PreflightException("公开成就缺少 API ID 或文本，请补充来源。");
                rows.Add(new(rows.Count + 1, apiName,
                    new Dictionary<string, AchievementTranslation> { [language] = new(name, description) }));
            }
        }
        var result = TranslationJson.FromRows(appId, variantId, rows);
        if (result["variant_id"]!.GetValue<string>() != variantId) throw new UsageException("Variant 无效。");
        return result;
    }

    internal static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new PreflightException("响应超过大小限制。");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token);
            if (count == 0) return output.ToArray();
            if (output.Length + count > limit) throw new PreflightException("响应超过大小限制。");
            output.Write(buffer, 0, count);
        }
    }

    [GeneratedRegex("<script\\b[^>]*\\bid\\s*=\\s*[\"']valve-ssr-data[\"'][^>]*>(.*?)</script\\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StructuredScript();
}
