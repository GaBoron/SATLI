using System.Text.Json;
using System.Text.RegularExpressions;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

public sealed record SchemaDraftRow(string ApiName, string Name, string Description);

public sealed record SchemaDraft(
    int Version,
    string AppId,
    string SourceSha256,
    string TargetLanguage,
    DateTimeOffset SavedAt,
    IReadOnlyList<SchemaDraftRow> Rows,
    string VariantId = "");

public sealed class SchemaDraftStore
{
    private const int CurrentVersion = 2;
    private static readonly Regex LanguagePattern =
        new("^[a-z][a-z0-9_]{1,31}$", RegexOptions.CultureInvariant);
    private readonly string _directory;

    public SchemaDraftStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            ApplicationDataPaths.DefaultDataDirectory,
            "schema-drafts");
    }

    public async Task<SchemaDraft> SaveAsync(
        SchemaInspection inspection,
        string targetLanguage,
        IEnumerable<AchievementEditorRow> rows)
    {
        var language = ValidateLanguage(targetLanguage);
        var draftRows = rows.Select(row => new SchemaDraftRow(
            row.ApiName,
            ValidateText(row.TargetName, row.ApiName, "名称"),
            ValidateText(row.TargetDescription, row.ApiName, "说明"))).ToArray();
        if (draftRows.Length != draftRows.Select(row => row.ApiName).Distinct().Count()
            || draftRows.Any(row => string.IsNullOrWhiteSpace(row.ApiName)))
        {
            throw new InvalidDataException("草稿包含空白或重复的成就 API ID。");
        }
        var draft = new SchemaDraft(
            CurrentVersion,
            inspection.AppId,
            inspection.SourceSha256,
            language,
            DateTimeOffset.UtcNow,
            draftRows,
            inspection.VariantId);
        var path = DraftPath(inspection, language);
        Directory.CreateDirectory(_directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true }));
            Satli.Core.FileSystem.FileOperations.ReplaceStaged(temporary, path);
        }
        finally
        {
            Satli.Core.FileSystem.RecycleBin.FileIfExists(temporary);
        }
        return draft;
    }

    public async Task<SchemaDraft?> LoadAsync(SchemaInspection inspection, string language)
    {
        var appId = inspection.AppId;
        var path = DraftPath(inspection, ValidateLanguage(language));
        if (!File.Exists(path)) path = DraftPath(appId);
        if (!File.Exists(path))
        {
            if (Directory.Exists(_directory) && Directory.EnumerateFiles(_directory, $"{appId}-*.json").Any())
                throw new InvalidDataException("没有与当前来源、Variant 和目标语言匹配的草稿。其他草稿保留，未自动套用。");
            return null;
        }
        try
        {
            var draft = JsonSerializer.Deserialize<SchemaDraft>(
                await File.ReadAllTextAsync(path));
            if (draft is null || draft.Version is not (1 or CurrentVersion) || draft.AppId != appId
                || (draft.Version == CurrentVersion && (draft.VariantId != inspection.VariantId || draft.TargetLanguage != language)))
            {
                throw new InvalidDataException("草稿版本或 App ID 无效。");
            }
            ValidateLanguage(draft.TargetLanguage);
            foreach (var row in draft.Rows)
            {
                ValidateText(row.Name, row.ApiName, "名称");
                ValidateText(row.Description, row.ApiName, "说明");
            }
            return draft;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("草稿 JSON 已损坏。", exception);
        }
    }

    public void Delete(SchemaInspection inspection, string language)
    {
        var path = DraftPath(inspection, ValidateLanguage(language));
        if (File.Exists(path))
        {
            Satli.Core.FileSystem.RecycleBin.FileIfExists(path);
        }
    }

    public static string? CompatibilityError(
        SchemaDraft draft,
        SchemaInspection inspection)
    {
        if (!string.Equals(
                draft.SourceSha256,
                inspection.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return "本地 schema 已变化，旧草稿未自动加载。";
        }
        var expected = inspection.Rows.Select(row => row.ApiName).ToHashSet(StringComparer.Ordinal);
        var actual = draft.Rows.Select(row => row.ApiName).ToHashSet(StringComparer.Ordinal);
        return expected.SetEquals(actual) && actual.Count == draft.Rows.Count
            ? null
            : "草稿的成就 ID 集合与当前 schema 不一致，未自动加载。";
    }

    private string DraftPath(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException("草稿 App ID 无效。", nameof(appId));
        }
        return Path.Combine(_directory, $"{appId}.json");
    }

    private string DraftPath(SchemaInspection inspection, string language)
    {
        var basePath = DraftPath(inspection.AppId);
        var identity = System.Text.Encoding.UTF8.GetBytes(
            $"bin\n{inspection.VariantId}\n{inspection.SourceSha256}\n{language}");
        return Path.Combine(_directory,
            $"{Path.GetFileNameWithoutExtension(basePath)}-{Satli.Core.FileSystem.FileOperations.Sha256(identity)}.json");
    }

    private static string ValidateLanguage(string value)
    {
        var language = value.Trim().ToLowerInvariant();
        if (!LanguagePattern.IsMatch(language) || language is "token" or "tokens")
        {
            throw new ArgumentException($"无效的 Steam 语言代码：{value}");
        }
        return language;
    }

    private static string ValidateText(string value, string apiName, string label)
    {
        if (value.Any(character => character is '\0' or '\r' or '\n' or '\t'
                                   || char.IsControl(character)))
        {
            throw new ArgumentException($"{apiName} 的{label}包含控制字符。");
        }
        return value;
    }
}
