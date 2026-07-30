namespace Satl_Gui.Models;

public enum RevisionDiffKind
{
    Unchanged,
    Added,
    Removed,
    Modified,
}

public sealed record RevisionDiffValue(
    string Previous,
    string Current,
    RevisionDiffKind Kind);

public sealed record SchemaRevisionDiffRow(
    int Index,
    string ApiName,
    RevisionDiffKind RowKind,
    RevisionDiffValue Name,
    RevisionDiffValue Description);

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

    public IReadOnlyList<SchemaRevisionDiffRow> RowsFor(string language)
    {
        if (_previous is null)
        {
            return _current.Rows.Select(row =>
            {
                var value = row.TranslationFor(language);
                return new SchemaRevisionDiffRow(
                    row.Index,
                    row.ApiName,
                    RevisionDiffKind.Unchanged,
                    Baseline(value.Name),
                    Baseline(value.Description));
            }).ToArray();
        }

        var previousRows = (_previous?.Rows ?? [])
            .ToDictionary(row => row.ApiName, StringComparer.Ordinal);
        var currentRows = _current.Rows
            .ToDictionary(row => row.ApiName, StringComparer.Ordinal);
        var orderedIds = currentRows.Keys
            .Concat(previousRows.Keys.Where(id => !currentRows.ContainsKey(id)))
            .OrderBy(id => currentRows.TryGetValue(id, out var current)
                ? current.Index
                : previousRows[id].Index);
        var rows = new List<SchemaRevisionDiffRow>();
        foreach (var apiName in orderedIds)
        {
            previousRows.TryGetValue(apiName, out var previousRow);
            currentRows.TryGetValue(apiName, out var currentRow);
            var previousHasLanguage = previousRow?.Translations.ContainsKey(language) == true;
            var currentHasLanguage = currentRow?.Translations.ContainsKey(language) == true;
            var previous = previousRow?.TranslationFor(language) ?? AchievementTranslation.Empty;
            var current = currentRow?.TranslationFor(language) ?? AchievementTranslation.Empty;
            rows.Add(new SchemaRevisionDiffRow(
                currentRow?.Index ?? previousRow!.Index,
                apiName,
                previousRow is null
                    ? RevisionDiffKind.Added
                    : currentRow is null
                        ? RevisionDiffKind.Removed
                        : RevisionDiffKind.Unchanged,
                Compare(previous.Name, current.Name, previousHasLanguage, currentHasLanguage),
                Compare(
                    previous.Description,
                    current.Description,
                    previousHasLanguage,
                    currentHasLanguage)));
        }
        return rows;
    }

    private static RevisionDiffValue Compare(
        string previous,
        string current,
        bool previousExists,
        bool currentExists)
    {
        var kind = (previousExists, currentExists) switch
        {
            (true, true) when previous == current => RevisionDiffKind.Unchanged,
            (true, true) => RevisionDiffKind.Modified,
            (true, false) => RevisionDiffKind.Removed,
            (false, true) => RevisionDiffKind.Added,
            _ => RevisionDiffKind.Unchanged,
        };
        return new RevisionDiffValue(DisplayValue(previous), DisplayValue(current), kind);
    }

    private static RevisionDiffValue Baseline(string value) =>
        new(DisplayValue(value), DisplayValue(value), RevisionDiffKind.Unchanged);

    private static string DisplayValue(string value) => string.IsNullOrEmpty(value) ? "（空）" : value;
}
