using Satli_Gui.Models;

namespace Satli_Gui.Services;

public sealed class AchievementEditState
{
    private string _language = string.Empty;
    private Dictionary<string, EditorTranslation> _acceptedRows =
        new(StringComparer.Ordinal);

    public void Accept(string language, IEnumerable<AchievementEditorRow> rows)
    {
        _language = language;
        _acceptedRows = rows.ToDictionary(
            row => row.ApiName,
            row => new EditorTranslation(row.TargetName, row.TargetDescription),
            StringComparer.Ordinal);
    }

    public bool IsDirty(string language, IEnumerable<AchievementEditorRow> rows)
    {
        if (!string.Equals(language, _language, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var count = 0;
        foreach (var row in rows)
        {
            count++;
            if (!_acceptedRows.TryGetValue(row.ApiName, out var accepted)
                || !string.Equals(row.TargetName, accepted.Name, StringComparison.Ordinal)
                || !string.Equals(
                    row.TargetDescription,
                    accepted.Description,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return count != _acceptedRows.Count;
    }
}
