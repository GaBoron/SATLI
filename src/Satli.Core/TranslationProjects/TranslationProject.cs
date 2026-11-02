using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.Formats;

namespace Satli.Core.TranslationProjects;

/// <summary>A source snapshot and target-language work, independent of Steam's local BIN state.</summary>
public sealed class TranslationProject
{
    public int Version { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AppId { get; set; } = "";
    public string GameName { get; set; } = "";
    public string VariantId { get; set; } = "default";
    public string TargetLanguage { get; set; } = "schinese";
    public string Source { get; set; } = "";
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public JsonObject? Original { get; set; }
    public string SourceFingerprint { get; set; } = "";
    public Dictionary<string, AchievementTranslation> Edits { get; set; } = new(StringComparer.Ordinal);
    public string PreparationError { get; set; } = "";

    public bool IsReady => Original is not null;

    public static TranslationProject Create(JsonObject original, string gameName, string target, string source)
    {
        var appId = original["app_id"]!.GetValue<string>();
        var validated = TranslationJson.Parse(TranslationJsonExport.Serialize(original), appId, allowIncomplete: true);
        return new TranslationProject
        {
            AppId = appId, GameName = gameName, TargetLanguage = target, Source = source,
            VariantId = validated["variant_id"]!.GetValue<string>(), Original = validated,
            SourceFingerprint = FileOperations.Sha256(TranslationJsonExport.Serialize(validated)),
        };
    }

    public JsonObject Build(bool forPublication)
    {
        if (Original is null) throw new UsageException("工程尚无可信成就来源，请先导入 JSON 或获取公开数据。");
        var originals = ReadRows(Original);
        var originalById = originals.ToDictionary(row => row.ApiName, StringComparer.Ordinal);
        var rows = originals.Select(row => new AchievementPreviewRow(row.Index, row.ApiName,
            new Dictionary<string, AchievementTranslation>(row.Translations, StringComparer.Ordinal)
            {
                [TargetLanguage] = Edits.GetValueOrDefault(row.ApiName)
                    ?? row.Translations.GetValueOrDefault(TargetLanguage) ?? new("", ""),
            })).ToArray();
        if (forPublication)
        {
            foreach (var row in rows)
            {
                var sources = originalById[row.ApiName].Translations.Where(pair => pair.Key != TargetLanguage)
                    .Select(pair => pair.Value).ToArray();
                TranslationTextValidation.Validate(row.ApiName, row.Translations[TargetLanguage],
                    sources.Length == 0 ? originalById[row.ApiName].Translations.Values : sources);
            }
        }
        return TranslationJson.FromRows(AppId, VariantId, rows, allowIncomplete: !forPublication);
    }

    public static IReadOnlyList<AchievementPreviewRow> ReadRows(JsonObject original) =>
        original["achievements"]!.AsObject().Select((pair, index) => new AchievementPreviewRow(index + 1, pair.Key,
            pair.Value!["translations"]!.AsObject().ToDictionary(item => item.Key,
                item => new AchievementTranslation(item.Value!["name"]!.GetValue<string>(),
                    item.Value!["description"]!.GetValue<string>()), StringComparer.Ordinal))).ToArray();

    /// <summary>Explicit migration copies only exact API IDs. The caller retains the old project on disk.</summary>
    public TranslationProject Rebase(JsonObject original, string source)
    {
        var next = Create(original, GameName, TargetLanguage, source);
        if (next.AppId != AppId || next.VariantId != VariantId)
            throw new UsageException("新来源的 App ID 或 Variant 与工程不一致。");
        var ids = original["achievements"]!.AsObject();
        next.Edits = Edits.Where(pair => ids.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return next;
    }

    public string DescribeChanges(JsonObject next)
    {
        var previous = Original?["achievements"]?.AsObject() ?? new JsonObject();
        var current = next["achievements"]!.AsObject();
        var added = current.Where(pair => !previous.ContainsKey(pair.Key)).Select(pair => pair.Key).ToArray();
        var removed = previous.Where(pair => !current.ContainsKey(pair.Key)).Select(pair => pair.Key).ToArray();
        var changed = current.Where(pair => previous.ContainsKey(pair.Key) && !JsonNode.DeepEquals(previous[pair.Key], pair.Value))
            .Select(pair => pair.Key).ToArray();
        static string List(string label, string[] ids) => $"{label} {ids.Length} 项：{string.Join(", ", ids.Take(20))}"
            + (ids.Length > 20 ? "（仅显示前 20 项）" : "");
        return $"{List("新增", added)}\n{List("移除", removed)}\n{List("原文变化", changed)}";
    }
}
