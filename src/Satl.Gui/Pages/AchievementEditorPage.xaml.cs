using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.Pages;

public sealed partial class AchievementEditorPage : Page
{
    private readonly SchemaEditorService _editor = new();
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
        ReferenceLanguageBox.ItemsSource = _inspection.Languages;
        TargetLanguageBox.ItemsSource = _inspection.Languages;
        var reference = _inspection.Languages.Contains("english", StringComparer.OrdinalIgnoreCase)
            ? "english"
            : _inspection.Languages.FirstOrDefault() ?? string.Empty;
        ReferenceLanguageBox.SelectedItem = reference;
        _targetLanguage = _inspection.Languages.Contains("schinese", StringComparer.OrdinalIgnoreCase)
            ? "schinese"
            : _inspection.Languages.FirstOrDefault() ?? "schinese";
        TargetLanguageBox.SelectedItem = _targetLanguage;
        foreach (var row in _inspection.Rows)
        {
            row.PropertyChanged += Row_PropertyChanged;
            row.SelectReference(reference);
            row.SelectTarget(_targetLanguage);
        }
        await RestoreDraftAsync();
        ApplyFilter();
        RestoreButton.IsEnabled = _inspection.CanRestore;
        _editState.Accept(_targetLanguage, _inspection.Rows);
        UpdateStatus();
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
        TargetLanguageBox.SelectedItem = _inspection!.Languages.FirstOrDefault(language =>
            string.Equals(language, _targetLanguage, StringComparison.OrdinalIgnoreCase));
        TargetLanguageBox.Text = _targetLanguage;
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

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AchievementEditorRow.TargetName)
            or nameof(AchievementEditorRow.TargetDescription))
        {
            UpdateStatus();
        }
    }

    private void ApplyFilter()
    {
        VisibleRows.Clear();
        if (_inspection is null)
        {
            return;
        }
        var query = SearchBox.Text.Trim();
        foreach (var row in _inspection.Rows.Where(row =>
                     string.IsNullOrWhiteSpace(query)
                     || row.ApiName.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || row.ReferenceName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || row.ReferenceDescription.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || row.TargetName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || row.TargetDescription.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            VisibleRows.Add(row);
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        ApplyFilter();

    private void ReferenceLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspection is null || ReferenceLanguageBox.SelectedItem is not string language)
        {
            return;
        }
        foreach (var row in _inspection.Rows)
        {
            row.SelectReference(language);
        }
    }

    private async void TargetLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingLanguage || _inspection is null || TargetLanguageBox.SelectedItem is not string language)
        {
            return;
        }
        await SelectTargetLanguageAsync(language);
    }

    private async void TargetLanguageBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_changingLanguage || _inspection is null)
        {
            return;
        }
        var value = TargetLanguageBox.Text.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(value) && value != _targetLanguage)
        {
            await SelectTargetLanguageAsync(value);
        }
    }

    private async Task SelectTargetLanguageAsync(string language)
    {
        if (_changingLanguage)
        {
            return;
        }
        _changingLanguage = true;
        try
        {
            language = language.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(language, "^[a-z][a-z0-9_]{1,31}$")
                || language is "token" or "tokens")
            {
                App.ViewModel.ShowInfo($"无效的 Steam 语言代码：{language}", InfoBarSeverity.Error);
                return;
            }
            if (language == _targetLanguage)
            {
                return;
            }
            if (HasUnsavedChanges
                && !await ConfirmAsync(
                    "切换目标语言",
                    "当前未保存的修改将被放弃。是否继续？",
                    "继续"))
            {
                TargetLanguageBox.SelectedItem = _targetLanguage;
                TargetLanguageBox.Text = _targetLanguage;
                return;
            }
            _targetLanguage = language;
            foreach (var row in _inspection!.Rows)
            {
                row.SelectTarget(language);
            }
            _editState.Accept(_targetLanguage, _inspection.Rows);
            UpdateStatus();
        }
        finally
        {
            _changingLanguage = false;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveChangesAsync();
    }

    private async void SaveDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_inspection is null)
        {
            return;
        }
        await RunBusyAsync(async () =>
        {
            var draft = await _drafts.SaveAsync(
                _inspection,
                _targetLanguage,
                _inspection.Rows);
            _editState.Accept(_targetLanguage, _inspection.Rows);
            UpdateStatus();
            App.ViewModel.ShowInfo(
                $"已保存 {_targetLanguage} 草稿（{draft.Rows.Count} 个成就）。",
                InfoBarSeverity.Success);
        });
    }

    private async Task<bool> SaveChangesAsync()
    {
        if (!await ConfirmIncompleteAsync("保存到本机"))
        {
            return false;
        }
        if (!await ConfirmAsync(
                "写回本地成就文件",
                "请先从系统托盘正常退出 Steam。应用会在写回前创建可恢复备份。",
                "确认写回"))
        {
            return false;
        }
        var saved = false;
        await RunBusyAsync(async () =>
        {
            var result = await _editor.ApplyAsync(
                _inspection!, _targetLanguage, _inspection!.Rows, allowIncomplete: true);
            App.ViewModel.ShowInfo(
                string.IsNullOrWhiteSpace(result.RevisionWarning)
                    ? $"已保存本地编辑并记录 Git 修订：修改 {result.ChangedFields} 个字段；备份位于 {result.Backup}"
                    : $"已保存本地编辑：修改 {result.ChangedFields} 个字段；{result.RevisionWarning}",
                string.IsNullOrWhiteSpace(result.RevisionWarning)
                    ? InfoBarSeverity.Success
                    : InfoBarSeverity.Warning);
            try
            {
                _drafts.Delete(_inspection.AppId);
            }
            catch (Exception exception)
            {
                await App.Logs.WriteAsync("警告", "成就草稿", $"本机写回成功，但删除草稿失败：{exception.Message}");
                await App.Logs.WriteExceptionDetailsAsync("成就草稿", exception);
            }
            await LoadCoreAsync();
            saved = true;
        });
        return saved;
    }

    private async void ExportBin_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("bin");

    private async void ExportZip_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("zip");

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (_game is not null)
        {
            Frame.Navigate(typeof(RevisionHistoryPage), _game);
        }
    }

    private async Task ExportAsync(string format)
    {
        if (!await ConfirmIncompleteAsync(format == "bin" ? "导出 BIN" : "导出投稿 ZIP"))
        {
            return;
        }
        var output = await PickDestinationAsync(format);
        if (output is null)
        {
            return;
        }
        await RunBusyAsync(async () =>
        {
            var result = await _editor.ExportAsync(
                _inspection!, _targetLanguage, _inspection!.Rows, true, format, output);
            App.ViewModel.ShowInfo(
                string.IsNullOrWhiteSpace(result.RevisionWarning)
                    ? $"已导出并记录 Git 修订：{result.Output}"
                    : $"已导出：{result.Output}；{result.RevisionWarning}",
                string.IsNullOrWhiteSpace(result.RevisionWarning)
                    ? InfoBarSeverity.Success
                    : InfoBarSeverity.Warning);
            if (format == "zip" && string.IsNullOrWhiteSpace(result.RevisionWarning))
            {
                await OfferContributionAsync(result);
            }
        });
    }

    private async Task OfferContributionAsync(SchemaEditResult result)
    {
        var draft = _contributions.Prepare(_game!, result);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "投稿到社区",
            Content = $"ZIP 已完成结构、语言和 SHA-256 校验。\n\n"
                + $"类型：{(draft.IsUpdate ? "更新已有译本" : "新译本投稿")}\n"
                + $"完整语言：{draft.Languages}\n"
                + $"摘要：{draft.Summary}\n\n"
                + "打开表单后，请把资源管理器中选中的 ZIP 拖入附件区域，并由你确认提交。",
            PrimaryButtonText = "打开投稿表单",
            CloseButtonText = "稍后投稿",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _contributions.OpenAsync(draft);
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_inspection is null || !await ConfirmAsync(
                "恢复上次编辑",
                "应用将恢复最近一次编辑前的校验备份。请先退出 Steam。",
                "恢复"))
        {
            return;
        }
        await RunBusyAsync(async () =>
        {
            try
            {
                var result = await _editor.RestoreAsync(_inspection.AppId, force: false);
                ShowRestoreResult(result);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("发生变化"))
            {
                if (!await ConfirmAsync(
                        "当前文件已变化",
                        exception.Message + Environment.NewLine + "强制恢复会先归档当前文件。是否继续？",
                        "强制恢复"))
                {
                    return;
                }
                var result = await _editor.RestoreAsync(_inspection.AppId, force: true);
                ShowRestoreResult(result);
            }
            await LoadCoreAsync();
        });
    }

    private static void ShowRestoreResult(SchemaEditResult result) =>
        App.ViewModel.ShowInfo(
            string.IsNullOrWhiteSpace(result.RevisionWarning)
                ? "已恢复上一次本地编辑，并记录 Git 修订。"
                : $"已恢复上一次本地编辑；{result.RevisionWarning}",
            string.IsNullOrWhiteSpace(result.RevisionWarning)
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning);

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges && !await ResolveUnsavedChangesAsync("返回本地游戏页"))
        {
            return;
        }
        if (Frame.CanGoBack)
        {
            _allowNavigation = true;
            Frame.GoBack();
        }
    }

    private async void Frame_Navigating(object? sender, NavigatingCancelEventArgs e)
    {
        if (_allowNavigation || !HasUnsavedChanges)
        {
            return;
        }
        e.Cancel = true;
        if (!await ResolveUnsavedChangesAsync("离开成就编辑器"))
        {
            return;
        }
        _allowNavigation = true;
        if (e.NavigationMode == NavigationMode.Back && Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(e.SourcePageType, e.Parameter, e.NavigationTransitionInfo);
        }
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !HasUnsavedChanges)
        {
            return;
        }
        args.Cancel = true;
        if (!await ResolveUnsavedChangesAsync("关闭应用"))
        {
            return;
        }
        _allowClose = true;
        App.Window.Close();
    }

    private async Task<bool> ResolveUnsavedChangesAsync(string destination)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "未保存的修改",
            Content = new TextBlock
            {
                Text = $"仍有未保存的成就修改。要先保存、放弃修改并{destination}，还是取消？",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "保存",
            SecondaryButtonText = "放弃",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => await SaveChangesAsync(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    private async Task<bool> ConfirmIncompleteAsync(string action)
    {
        if (_inspection is null)
        {
            return false;
        }
        var missingNames = _inspection.Rows.Count(row => string.IsNullOrEmpty(row.TargetName));
        var missingDescriptions = _inspection.Rows.Count(row => string.IsNullOrEmpty(row.TargetDescription));
        if (missingNames == 0 && missingDescriptions == 0)
        {
            return true;
        }
        return await ConfirmAsync(
            "目标语言内容不完整",
            $"缺少名称 {missingNames} 项，缺少说明 {missingDescriptions} 项。仍要{action}吗？",
            "继续");
    }

    private async Task<string?> PickDestinationAsync(string format)
    {
        try
        {
            var extension = format == "bin" ? ".bin" : ".zip";
            return NativeFilePickerService.PickSaveFile(
                App.WindowHandle,
                format == "bin" ? "导出 Steam 成就 schema" : "导出投稿 ZIP",
                $"UserGameStatsSchema_{_inspection!.AppId}{extension}",
                format == "bin" ? "Steam 成就 schema" : "投稿 ZIP",
                extension);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法打开保存位置选择器：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("文件选择器", exception);
            return null;
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_isBusy)
        {
            return;
        }
        _isBusy = true;
        BusyProgress.Visibility = Visibility.Visible;
        PageLayout.IsHitTestVisible = false;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"成就编辑操作失败：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("成就编辑", exception);
        }
        finally
        {
            PageLayout.IsHitTestVisible = true;
            BusyProgress.Visibility = Visibility.Collapsed;
            _isBusy = false;
        }
    }

    private void UpdateStatus()
    {
        if (_inspection is null)
        {
            StatusText.Text = "正在加载…";
            return;
        }
        var missingNames = _inspection.Rows.Count(row => string.IsNullOrEmpty(row.TargetName));
        var missingDescriptions = _inspection.Rows.Count(row => string.IsNullOrEmpty(row.TargetDescription));
        StatusText.Text =
            $"目标语言 {_targetLanguage} · 显示 {VisibleRows.Count}/{_inspection.Rows.Count} · " +
            $"缺少名称 {missingNames} · 缺少说明 {missingDescriptions}" +
            (HasUnsavedChanges ? " · 有未保存修改" : string.Empty);
    }

    private bool HasUnsavedChanges =>
        _inspection is not null
        && _editState.IsDirty(_targetLanguage, _inspection.Rows);
}
