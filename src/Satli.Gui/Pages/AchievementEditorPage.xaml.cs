using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.Pages;

public sealed partial class AchievementEditorPage : Page
{
    private readonly SchemaEditorService _editor = new();
    private readonly SteamMutationDialogService _steamMutations = new();
    private readonly SchemaDraftStore _drafts = new();
    private readonly ContributionWorkflowService _contributions = new();
    private readonly AchievementEditState _editState = new();
    private SchemaInspection? _inspection;
    private GameItem? _game;
    private bool _isBusy;
    private bool _changingLanguage;
    private bool _allowNavigation;
    private bool _allowClose;
    private string _targetLanguage = "schinese";

    public ObservableCollection<AchievementEditorRow> VisibleRows { get; } = [];

    public AchievementEditorPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _game = e.Parameter as GameItem;
        if (_game is null)
        {
            SetBusyState(false);
            App.ViewModel.ShowInfo("无法打开成就编辑器：缺少游戏信息。", InfoBarSeverity.Error);
            return;
        }
        Frame.Navigating += Frame_Navigating;
        App.Window.AppWindow.Closing += AppWindow_Closing;
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Frame.Navigating -= Frame_Navigating;
        App.Window.AppWindow.Closing -= AppWindow_Closing;
        base.OnNavigatedFrom(e);
    }

    private async Task LoadAsync()
    {
        if (_game is null)
        {
            return;
        }
        await RunBusyAsync(LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        var game = _game ?? throw new InvalidOperationException("缺少待编辑游戏。");
        _inspection = await _editor.InspectAsync(game);
        TitleText.Text = $"编辑 {game.GameName}";
        MetadataText.Text =
            $"App ID {_game.AppId} · {_inspection.Rows.Count} 个成就 · SHA-256 {_inspection.SourceSha256}";
        var referenceLanguageOptions = _inspection.Languages
            .Select(SteamLanguageCatalog.CreateOption)
            .ToArray();
        ReferenceLanguageBox.ItemsSource = referenceLanguageOptions;
        TargetLanguageBox.ItemsSource = SteamLanguageCatalog.CreateEditorOptions(
            _inspection.Languages);
        var reference = _inspection.Languages.Contains("english", StringComparer.OrdinalIgnoreCase)
            ? "english"
            : _inspection.Languages.FirstOrDefault() ?? string.Empty;
        ReferenceLanguageBox.SelectedItem = referenceLanguageOptions.FirstOrDefault(option =>
            option.Code.Equals(reference, StringComparison.OrdinalIgnoreCase));
        _targetLanguage = _inspection.Languages.Contains("schinese", StringComparer.OrdinalIgnoreCase)
            ? "schinese"
            : _inspection.Languages.FirstOrDefault() ?? "schinese";
        SelectTargetLanguageOption(_targetLanguage);
        foreach (var row in _inspection.Rows)
        {
            row.PropertyChanged += Row_PropertyChanged;
            row.SelectReference(reference);
            row.SelectTarget(_targetLanguage);
        }
        await RestoreDraftAsync();
        ApplyFilter();
        _editState.Accept(_targetLanguage, _inspection.Rows);
        UpdateStatus();
        await App.Logs.WriteAsync(
            "详细",
            "成就编辑",
            $"编辑器已加载。App ID={game.AppId}；游戏={game.GameName}；成就数={_inspection.Rows.Count}；" +
            $"语言={string.Join(", ", _inspection.Languages)}；目标语言={_targetLanguage}。",
            detailed: true);
    }

    private async Task RestoreDraftAsync()
    {
        SchemaDraft? draft;
        try
        {
            draft = await _drafts.LoadAsync(_inspection!.AppId);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法读取成就草稿：{exception.Message}", InfoBarSeverity.Warning);
            await App.Logs.WriteExceptionDetailsAsync("成就草稿", exception);
            return;
        }
        if (draft is null)
        {
            return;
        }
        var compatibilityError = SchemaDraftStore.CompatibilityError(draft, _inspection!);
        if (compatibilityError is not null)
        {
            App.ViewModel.ShowInfo(compatibilityError, InfoBarSeverity.Warning);
            return;
        }

        _changingLanguage = true;
        _targetLanguage = draft.TargetLanguage;
        SelectTargetLanguageOption(_targetLanguage);
        var values = draft.Rows.ToDictionary(row => row.ApiName, StringComparer.Ordinal);
        foreach (var row in _inspection.Rows)
        {
            row.SelectTarget(_targetLanguage);
            row.TargetName = values[row.ApiName].Name;
            row.TargetDescription = values[row.ApiName].Description;
        }
        _changingLanguage = false;
        App.ViewModel.ShowInfo(
            $"已恢复 {_targetLanguage} 草稿（保存于 {draft.SavedAt.ToLocalTime():g}）。",
            InfoBarSeverity.Informational);
    }

    private void SelectTargetLanguageOption(string language)
    {
        var option = (TargetLanguageBox.ItemsSource as IEnumerable<SteamLanguageOption>)
            ?.FirstOrDefault(item =>
                item.Code.Equals(language, StringComparison.OrdinalIgnoreCase));
        TargetLanguageBox.SelectedItem = option;
        TargetLanguageBox.Text = option?.DisplayName ?? language;
    }
}
