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
        try
        {
            using var document = JsonDocument.Parse(payload);
            CheckUniqueKeys(document.RootElement);
            var root = document.RootElement;
            var version = root.GetProperty("version").GetInt32();
            if (version != asset.Version || version is not (1 or 2)
                || root.GetProperty("app_id").GetString() != appId
                || root.GetProperty("variant_id").GetString() != variant.VariantId)
                throw new IntegrityException("JSON 格式版本、App ID 或 Variant 与目录不一致");
            if (version == 1)
            {
                if (!variant.HasBin || root.GetProperty("source_sha256").GetString() != variant.Sha256)
                    throw new IntegrityException("JSON 的源 BIN 标识与目录不一致");
            }
            else if (root.EnumerateObject().Count() != 5 || root.TryGetProperty("source_sha256", out _))
                throw new IntegrityException("格式 2 JSON 只能包含五个标准显示文本字段");

            var languages = ReadLanguages(root.GetProperty("languages"));
            if (variant.Languages is null || !languages.SequenceEqual(variant.Languages))
                throw new IntegrityException("JSON 语言与目录不一致");
            var achievements = root.GetProperty("achievements");
            if (achievements.ValueKind != JsonValueKind.Object)
                throw new IntegrityException("JSON 成就必须按 API name 索引");
            var count = 0;
            foreach (var achievement in achievements.EnumerateObject())
            {
                if (achievement.Name.Length == 0 || achievement.Name.EnumerateRunes().Count() > 512 || achievement.Name.Any(char.IsControl))
                    throw new IntegrityException("JSON 成就 API name 无效");
                var translations = achievement.Value.GetProperty("translations");
                if (translations.EnumerateObject().Count() != languages.Count
                    || translations.EnumerateObject().Any(item => !languages.Contains(item.Name)))
                    throw new IntegrityException("JSON 包含未声明或遗漏的语言");
                foreach (var field in new[] { "name", "description" })
                {
                    var populated = 0;
                    foreach (var language in languages)
                    {
                        var text = translations.GetProperty(language).GetProperty(field).GetString()
                            ?? throw new IntegrityException("JSON 文本字段不能是 null");
                        if (text.EnumerateRunes().Count() > 65536 || text.Contains('\0'))
                            throw new IntegrityException("JSON 文本过长或含 NUL");
                        if (!string.IsNullOrWhiteSpace(text)) populated++;
                    }
                    if (populated != 0 && populated != languages.Count)
                        throw new IntegrityException("JSON 的名称或描述存在未完成的语言覆盖");
                }
                count++;
            }
            if (count is < 1 or > 10000 || count != variant.AchievementCount)
                throw new IntegrityException("JSON 成就数量与目录不一致");
            return JsonNode.Parse(payload)!.AsObject();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or CatalogException)
        {
            throw new IntegrityException("显示文本 JSON 不符合翻译库规范", exception);
        }
    }

    public static IReadOnlyList<string> ReadLanguages(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new CatalogException("语言列表必须是数组");
        var languages = value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        if (languages.Length is < 1 or > 64
            || languages.Any(item => !LanguagePattern().IsMatch(item))
            || !languages.SequenceEqual(languages.Distinct().Order(StringComparer.Ordinal)))
            throw new CatalogException("Steam 语言代码必须按字母序排列且不重复");
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

    [GeneratedRegex("^[a-z][a-z0-9_]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();
}
