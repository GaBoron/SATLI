namespace Satl_Gui.Models;

public enum RevisionDiffLineKind
{
    Removed,
    Added,
}

public sealed record RevisionDiffLine(
    int Index,
    string ApiName,
    string Field,
    string Text,
    RevisionDiffLineKind Kind)
{
    public string Prefix => Kind == RevisionDiffLineKind.Removed ? "−" : "+";
}

public sealed class SchemaRevisionDiff
{
    private readonly ReplacementPreview? _previous;
    private readonly ReplacementPreview _current;

    public SchemaRevisionDiff(ReplacementPreview? previous, ReplacementPreview current)
    {
        _previous = previous;
        _current = current;
        Languages = (previous?.Languages ?? [])
            .Concat(current.Languages)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool HasParent => _previous is not null;
    public IReadOnlyList<string> Languages { get; }
    public string DefaultLanguage =>
        Languages.FirstOrDefault(language =>
            language.Equals("schinese", StringComparison.OrdinalIgnoreCase))
        ?? Languages.FirstOrDefault()
        ?? "schinese";

    public IReadOnlyList<RevisionDiffLine> LinesFor(string language)
    {
        var previousRows = (_previous?.Rows ?? [])
            .ToDictionary(row => row.ApiName, StringComparer.Ordinal);
        var currentRows = _current.Rows
            .ToDictionary(row => row.ApiName, StringComparer.Ordinal);
        var orderedIds = previousRows.Keys
            .Concat(currentRows.Keys.Where(id => !previousRows.ContainsKey(id)));
        var lines = new List<RevisionDiffLine>();
        foreach (var apiName in orderedIds)
        {
            previousRows.TryGetValue(apiName, out var previousRow);
            currentRows.TryGetValue(apiName, out var currentRow);
            var previousHasLanguage = previousRow?.Translations.ContainsKey(language) == true;
            var currentHasLanguage = currentRow?.Translations.ContainsKey(language) == true;
            var previous = previousRow?.TranslationFor(language) ?? AchievementTranslation.Empty;
            var current = currentRow?.TranslationFor(language) ?? AchievementTranslation.Empty;
            AddField(lines, previousRow, currentRow, apiName, "名称", previous.Name, current.Name,
                previousHasLanguage, currentHasLanguage);
            AddField(lines, previousRow, currentRow, apiName, "说明", previous.Description,
                current.Description, previousHasLanguage, currentHasLanguage);
        }
        return lines;
    }

    private static void AddField(
        ICollection<RevisionDiffLine> lines,
        AchievementPreviewRow? previousRow,
        AchievementPreviewRow? currentRow,
        string apiName,
        string field,
        string previous,
        string current,
        bool previousExists,
        bool currentExists)
    {
        if (previousExists && currentExists && previous == current)
        {
            return;
        }
        if (previousExists)
        {
            lines.Add(new RevisionDiffLine(
                previousRow!.Index, apiName, field, DisplayValue(previous), RevisionDiffLineKind.Removed));
        }
        if (currentExists)
        {
            lines.Add(new RevisionDiffLine(
                currentRow!.Index, apiName, field, DisplayValue(current), RevisionDiffLineKind.Added));
        }
    }

    private static string DisplayValue(string value) => string.IsNullOrEmpty(value) ? "（空）" : value;
}
