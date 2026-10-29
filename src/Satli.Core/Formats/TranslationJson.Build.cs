using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using Satli.Core.FileSystem;

namespace Satli.Core.Formats;

/// <summary>Builds display text from losslessly parsed schemas or editor rows.</summary>
public static partial class TranslationJson
{
    public static JsonObject FromBin(byte[] payload, string appId, string variantId = "local", bool standalone = false)
    {
        var nodes = BinaryKeyValues.Parse(payload);
        if (!BinaryKeyValues.Serialize(nodes).AsSpan().SequenceEqual(payload))
            throw new IntegrityException("BIN 未通过字节级 roundtrip 校验，拒绝转换");
        var rows = new List<AchievementPreviewRow>();
        foreach (var bits in BinaryKeyValues.Walk(nodes).Where(node => node.TypeId == 0 && node.Name == "bits"))
        {
            foreach (var achievement in bits.Children.Where(node => node.TypeId == 0))
            {
                var apiName = BinaryKeyValues.FirstString(achievement, "name");
                if (string.IsNullOrEmpty(apiName)) throw new IntegrityException("BIN 中的成就缺少 API name，拒绝转换");
                var names = LanguageValues(BinaryKeyValues.NestedObject(achievement, "display", "name"));
                var descriptions = LanguageValues(BinaryKeyValues.NestedObject(achievement, "display", "desc"));
                var translations = names.Keys.Concat(descriptions.Keys).Distinct(StringComparer.Ordinal)
                    .ToDictionary(language => language, language => new AchievementTranslation(
                        names.GetValueOrDefault(language, ""), descriptions.GetValueOrDefault(language, "")), StringComparer.Ordinal);
                if (translations.Count == 0) throw new IntegrityException("BIN 中的成就没有可转换的语言文本");
                rows.Add(new(rows.Count + 1, apiName, translations));
            }
        }
        return FromRows(appId, variantId, rows, standalone ? null : FileOperations.Sha256(payload));
    }

    private static Dictionary<string, string> LanguageValues(BinaryKeyValuesNode? node)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is null) return values;
        foreach (var child in node.Children.Where(child => child.TypeId == 1))
        {
            var language = child.Name.ToLowerInvariant();
            if (language is "token" or "tokens") continue;
            if (!values.TryAdd(language, child.Value ?? ""))
                throw new IntegrityException("BIN 含重复语言映射，拒绝转换");
        }
        return values;
    }

    public static JsonObject FromRows(string appId, string variantId,
        IReadOnlyList<AchievementPreviewRow> rows, string? sourceSha256 = null)
    {
        var languages = rows.SelectMany(row => row.Translations.Keys)
            .Select(language => language.ToLowerInvariant()).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var achievements = new JsonObject();
        foreach (var row in rows)
        {
            if (achievements.ContainsKey(row.ApiName))
                throw new IntegrityException("本地译文含重复成就 API name，拒绝转换");
            var values = new Dictionary<string, AchievementTranslation>(StringComparer.Ordinal);
            foreach (var pair in row.Translations)
            {
                if (!values.TryAdd(pair.Key.ToLowerInvariant(), pair.Value))
                    throw new IntegrityException("本地译文含重复语言映射，拒绝转换");
            }
            var translations = new JsonObject();
            foreach (var language in languages)
            {
                var text = values.GetValueOrDefault(language);
                translations[language] = new JsonObject
                {
                    ["name"] = text?.Name ?? "", ["description"] = text?.Description ?? "",
                };
            }
            achievements[row.ApiName] = new JsonObject { ["translations"] = translations };
        }
        var translation = new JsonObject
        {
            ["version"] = sourceSha256 is null ? 2 : 1,
            ["app_id"] = appId,
            ["variant_id"] = VariantPattern().IsMatch(variantId) ? variantId : "local",
            ["languages"] = new JsonArray(languages.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
            ["achievements"] = achievements,
        };
        if (sourceSha256 is not null) translation["source_sha256"] = sourceSha256;
        // Missing text stays empty; no source language is silently substituted.
        // Apply the byte limit to UTF-8 text, rather than an ASCII-escaped expansion of that text.
        return Parse(JsonSerializer.SerializeToUtf8Bytes(translation,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), appId);
    }
}
