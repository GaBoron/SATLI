using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Satli.Core.FileSystem;
using Satli.Core.Models;

namespace Satli.Core.Formats;

/// <summary>Validates display text independently of a local Steam schema or plugin.</summary>
public static partial class TranslationJson
{
    public const int MaximumBytes = 32 * 1024 * 1024;

    public static JsonObject Parse(byte[] payload, string appId, SchemaVariant variant)
    {
        var asset = variant.Json ?? throw new IntegrityException("此版本未提供显示文本 JSON");
        if (payload.Length != asset.Size || payload.Length > MaximumBytes)
            throw new IntegrityException("JSON 大小与目录不一致，请刷新目录后重试");
        if (asset.Sha256 is not null && FileOperations.Sha256(payload) != asset.Sha256)
            throw new IntegrityException("JSON 内容完整性校验失败，请刷新目录后重试");
        var translation = Parse(payload, appId);
        if (translation["version"]!.GetValue<int>() != asset.Version
            || translation["variant_id"]!.GetValue<string>() != variant.VariantId)
            throw new IntegrityException("JSON 格式版本或 Variant 与目录不一致");
        if (asset.Version == 1 && (!variant.HasBin || translation["source_sha256"]!.GetValue<string>() != variant.Sha256))
            throw new IntegrityException("JSON 的源 BIN 标识与目录不一致");
        var languages = translation["languages"]!.AsArray().Select(item => item!.GetValue<string>()).Order(StringComparer.Ordinal);
        if (variant.Languages is null || !languages.SequenceEqual(variant.Languages.Order(StringComparer.Ordinal))
            || translation["achievements"]!.AsObject().Count != variant.AchievementCount)
            throw new IntegrityException("JSON 语言或成就数量与目录不一致");
        return translation;
    }

    /// <summary>Validates the public lite translation format without requiring catalog metadata or a local BIN.</summary>
    public static JsonObject Parse(byte[] payload, string appId)
    {
        if (payload.Length is < 1 or > MaximumBytes)
            throw new IntegrityException("显示文本 JSON 大小无效");
        try
        {
            using var document = JsonDocument.Parse(payload);
            CheckUniqueKeys(document.RootElement);
            var root = document.RootElement;
            var version = root.GetProperty("version").GetInt32();
            if (version is not (1 or 2) || !AppIdPattern().IsMatch(appId)
                || root.GetProperty("app_id").GetString() != appId
                || !VariantPattern().IsMatch(root.GetProperty("variant_id").GetString() ?? ""))
                throw new IntegrityException("JSON 格式版本、App ID 或 Variant 无效");
            if (version == 1)
            {
                if (!HashPattern().IsMatch(root.GetProperty("source_sha256").GetString() ?? ""))
                    throw new IntegrityException("JSON 的源 BIN 标识无效");
            }
            else if (root.EnumerateObject().Count() != 5 || root.TryGetProperty("source_sha256", out _))
                throw new IntegrityException("格式 2 JSON 只能包含五个标准显示文本字段");

            var languages = ReadLanguages(root.GetProperty("languages"), requireSorted: version == 2);
            var achievements = root.GetProperty("achievements");
            if (achievements.ValueKind != JsonValueKind.Object)
                throw new IntegrityException("JSON 成就必须按 API name 索引");
            var count = 0;
            foreach (var achievement in achievements.EnumerateObject())
            {
                if (achievement.Name.Length == 0 || achievement.Name.EnumerateRunes().Count() > 512 || achievement.Name.Any(char.IsControl))
                    throw new IntegrityException("JSON 成就 API name 无效");
                var translations = achievement.Value.GetProperty("translations");
                if (version == 2 && (translations.EnumerateObject().Count() != languages.Count
                    || translations.EnumerateObject().Any(item => !languages.Contains(item.Name))))
                    throw new IntegrityException("JSON 包含未声明或遗漏的语言");
                foreach (var field in new[] { "name", "description" })
                {
                    var populated = 0;
                    foreach (var language in languages)
                    {
                        var text = translations.GetProperty(language).GetProperty(field).GetString()
                            ?? throw new IntegrityException("JSON 文本字段不能是 null");
                        var tooLong = version == 1
                            ? Encoding.UTF8.GetByteCount(text) > (field == "name" ? 16384 : 65536)
                            : text.EnumerateRunes().Count() > 65536;
                        if (tooLong || text.Contains('\0'))
                            throw new IntegrityException("JSON 文本过长或含 NUL");
                        if (!string.IsNullOrWhiteSpace(text)) populated++;
                    }
                    if (version == 2 && populated != 0 && populated != languages.Count)
                        throw new IntegrityException("JSON 的名称或描述存在未完成的语言覆盖");
                }
                count++;
            }
            if (count < 1 || count > (version == 1 ? 100000 : 10000))
                throw new IntegrityException("JSON 成就数量无效");
            return JsonNode.Parse(payload)!.AsObject();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException or CatalogException)
        {
            throw new IntegrityException("显示文本 JSON 不符合翻译库规范", exception);
        }
    }

    public static IReadOnlyList<string> ReadLanguages(JsonElement value, bool requireSorted = true)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new CatalogException("语言列表必须是数组");
        var languages = value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        if (languages.Length is < 1 or > 64
            || languages.Any(item => !LanguagePattern().IsMatch(item))
            || languages.Distinct(StringComparer.Ordinal).Count() != languages.Length
            || (requireSorted && !languages.SequenceEqual(languages.Order(StringComparer.Ordinal))))
            throw new CatalogException("Steam 语言代码无效、重复或顺序错误");
        return languages;
    }

    public static JsonObject Preview(JsonObject translation)
    {
        var achievements = translation["achievements"]!.AsObject();
        return new JsonObject
        {
            ["achievement_count"] = achievements.Count,
            ["languages"] = translation["languages"]!.DeepClone(),
            ["rows"] = new JsonArray(achievements.Select((pair, index) => (JsonNode)new JsonObject
            {
                ["index"] = index + 1,
                ["api_name"] = pair.Key,
                ["translations"] = pair.Value!["translations"]!.DeepClone(),
            }).ToArray()),
        };
    }

    private static void CheckUniqueKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || property.Name.Contains('\0'))
                    throw new IntegrityException("JSON 含重复键或 NUL 键");
                CheckUniqueKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckUniqueKeys(item);
    }

    [GeneratedRegex("\\A[a-z][a-z0-9_]{1,31}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();

    [GeneratedRegex("\\A[1-9][0-9]{0,9}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdPattern();

    [GeneratedRegex("\\A[a-z0-9][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VariantPattern();

    [GeneratedRegex("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
