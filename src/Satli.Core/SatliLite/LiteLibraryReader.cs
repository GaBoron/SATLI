using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.Formats;

namespace Satli.Core.SatliLite;

public sealed record LiteLibrarySnapshot(long GeneratedAt, IReadOnlyList<JsonObject> Apps);

/// <summary>Reads the plugin-owned public v2 snapshot, never its private state or cache.</summary>
public sealed class LiteLibraryReader(string steamDirectory)
{
    private readonly string _root = Path.Combine(Path.GetFullPath(steamDirectory), "millennium", "config", "satli-lite", "external", "v2");

    public async Task<LiteLibrarySnapshot?> ReadAsync(CancellationToken token = default)
    {
        var path = Path.Combine(_root, "library.json");
        if (!File.Exists(path)) return null;
        var bytes = await ReadFileAsync(path, 4 * 1024 * 1024, token);
        var root = JsonNode.Parse(bytes)!.AsObject();
        if (root["protocol"]?.GetValue<int>() != 2 || root["plugin_id"]?.ToString() != "com.gaboron.satli-lite"
            || root["apps"] is not JsonArray apps || apps.Count > 10000
            || root["generated_at"] is not JsonValue time || !time.TryGetValue<long>(out var generated)
            || generated is < 0 or > 253402300799)
            throw new IntegrityException("lite 公开译文清单格式无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<JsonObject>();
        foreach (var node in apps)
        {
            var app = node?.AsObject() ?? throw new IntegrityException("lite 译文记录无效。");
            var id = app["app_id"]?.GetValue<string>() ?? "";
            if (!uint.TryParse(id, out var number) || number == 0 || number.ToString() != id || !ids.Add(id))
                throw new IntegrityException("lite 译文清单包含无效或重复 App ID。");
            LiteClient.ValidateApp(app);
            if (app["exists"]?.GetValue<bool>() != true) throw new IntegrityException("lite 清单包含未安装记录。");
            result.Add(app);
        }
        return new(generated, result);
    }

    public async Task<JsonObject> ReadTranslationAsync(string appId, CancellationToken token = default)
    {
        var snapshot = await ReadAsync(token) ?? throw new PreflightException("请更新 SATLI-lite，取得公开译文快照后重试。");
        var app = snapshot.Apps.SingleOrDefault(app => app["app_id"]?.ToString() == appId)
            ?? throw new PreflightException("lite 当前清单中没有此游戏，请刷新。");
        var revision = app["revision"]!.GetValue<string>();
        var filename = app["translation_file"]?.GetValue<string>() ?? "";
        if (revision.Length > 128 || !revision.All(value => char.IsAsciiLetterOrDigit(value) || value == '-')
            || filename != $"{appId}-{revision}.json")
            throw new PreflightException("此译文的公开快照尚不可读，请在 lite 中检查后刷新。");
        var bytes = await ReadFileAsync(Path.Combine(_root, filename), TranslationJson.MaximumBytes, token);
        var translation = TranslationJson.Parse(bytes, appId);
        if (translation["variant_id"]?.ToString() != app["variant_id"]?.ToString()
            || !translation["languages"]!.AsArray().Any(value => value?.ToString() == app["language"]?.ToString()))
            throw new IntegrityException("lite 公开译文与清单中的版本或语言不一致。");
        var latest = await ReadAsync(token);
        if (latest?.Apps.SingleOrDefault(item => item["app_id"]?.ToString() == appId)?["revision"]?.ToString() != revision)
            throw new PreflightException("lite 译文已变化，请刷新后重新打开。");
        return translation;
    }

    private static async Task<byte[]> ReadFileAsync(string path, int limit, CancellationToken token)
    {
        FileOperations.EnsureNoReparsePoints(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            65536, FileOptions.Asynchronous);
        if (input.Length > limit) throw new IntegrityException("lite 公开快照超过大小限制。");
        var bytes = new byte[(int)Math.Min(input.Length + 1, limit + 1L)];
        var count = await input.ReadAtLeastAsync(bytes, bytes.Length, false, token);
        if (count > limit) throw new IntegrityException("lite 公开快照超过大小限制。");
        return bytes[..count];
    }
}
