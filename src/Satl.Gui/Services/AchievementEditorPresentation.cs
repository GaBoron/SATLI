using System.Text.RegularExpressions;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public readonly record struct AchievementContentGaps(
    int MissingNames,
    int MissingDescriptions)
{
    public bool IsComplete => MissingNames == 0 && MissingDescriptions == 0;
}

public static partial class AchievementEditorPresentation
{
    public static IReadOnlyList<AchievementEditorRow> Filter(
        IEnumerable<AchievementEditorRow> rows,
        string? searchText)
    {
        var query = searchText?.Trim() ?? string.Empty;
        return rows.Where(row =>
                string.IsNullOrWhiteSpace(query)
                || row.ApiName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.ReferenceName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || row.ReferenceDescription.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || row.TargetName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || row.TargetDescription.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
    }

    public static bool TryNormalizeLanguage(
        string? value,
        out string language,
        out string error)
    {
        language = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!LanguagePattern().IsMatch(language) || language is "token" or "tokens")
        {
            error = $"无效的 Steam 语言代码：{language}";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public static AchievementContentGaps CountGaps(IEnumerable<AchievementEditorRow> rows)
    {
        var missingNames = 0;
        var missingDescriptions = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.TargetName))
            {
                missingNames++;
            }
            if (string.IsNullOrEmpty(row.TargetDescription))
            {
                missingDescriptions++;
            }
        }
        return new AchievementContentGaps(missingNames, missingDescriptions);
    }

    public static string BuildStatus(
        string targetLanguage,
        int visibleCount,
        IReadOnlyList<AchievementEditorRow> rows,
        bool hasUnsavedChanges)
    {
        var gaps = CountGaps(rows);
        return $"目标语言 {targetLanguage} · 显示 {visibleCount}/{rows.Count} · " +
            $"缺少名称 {gaps.MissingNames} · 缺少说明 {gaps.MissingDescriptions}" +
            (hasUnsavedChanges ? " · 有未保存修改" : string.Empty);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();
}
