using CommunityToolkit.Mvvm.ComponentModel;

namespace Satli_Gui.Models;

public sealed class AchievementEditorRow : ObservableObject
{
    private string _referenceName = string.Empty;
    private string _referenceDescription = string.Empty;
    private string _targetName = string.Empty;
    private string _targetDescription = string.Empty;

    public int Index { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, EditorTranslation> Translations { get; set; } =
        new Dictionary<string, EditorTranslation>();

    public string ReferenceName
    {
        get => _referenceName;
        set => SetProperty(ref _referenceName, value);
    }

    public string ReferenceDescription
    {
        get => _referenceDescription;
        set => SetProperty(ref _referenceDescription, value);
    }

    public string TargetName
    {
        get => _targetName;
        set => SetProperty(ref _targetName, value);
    }

    public string TargetDescription
    {
        get => _targetDescription;
        set => SetProperty(ref _targetDescription, value);
    }

    public void SelectReference(string language)
    {
        Translations.TryGetValue(language, out var translation);
        ReferenceName = translation?.Name ?? string.Empty;
        ReferenceDescription = translation?.Description ?? string.Empty;
    }

    public void SelectTarget(string language)
    {
        Translations.TryGetValue(language, out var translation);
        TargetName = translation?.Name ?? string.Empty;
        TargetDescription = translation?.Description ?? string.Empty;
    }
}

public sealed record EditorTranslation(string Name, string Description);

public sealed record SchemaInspection(
    string AppId,
    string SourcePath,
    string SourceSha256,
    bool CanRestore,
    IReadOnlyList<string> Languages,
    IReadOnlyList<AchievementEditorRow> Rows,
    string GameName = "",
    string VariantId = "");

public sealed record SchemaEditResult(
    string OutputSha256,
    int AchievementCount,
    int ChangedFields,
    int MissingNames,
    int MissingDescriptions,
    bool CanRestore,
    string? Output,
    string? Backup,
    int ChangedNames = 0,
    int ChangedDescriptions = 0,
    IReadOnlyList<string>? CompleteLanguages = null,
    string RevisionCommit = "",
    string RevisionWarning = "");
