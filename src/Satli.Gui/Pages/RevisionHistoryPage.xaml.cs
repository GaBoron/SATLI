using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.Pages;

public sealed partial class RevisionHistoryPage : Page
{
    private readonly SchemaRevisionService _revisions = new();
    private GameItem? _game;
    private bool _isBusy;

    public ObservableCollection<SchemaRevisionItem> Revisions { get; } = [];

    public RevisionHistoryPage()
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
            App.ViewModel.ShowInfo("无法打开修订历史：缺少游戏信息。", InfoBarSeverity.Error);
            return;
        }
        TitleText.Text = $"修订历史 · {_game.GameName}";
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await RunBusyAsync(LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        var items = await _revisions.ListAsync(_game!);
        Revisions.Clear();
        foreach (var item in items)
        {
            Revisions.Add(item);
        }
        StatusText.Text = items.Count == 0
            ? "尚无正式修订；保存或导出不同内容后会创建 Git 提交。"
            : $"共 {items.Count} 个 Git 修订。";
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SchemaRevisionItem revision } || _game is null)
        {
            return;
        }
        await RunBusyAsync(async () =>
        {
            var diff = await _revisions.PreviewDiffAsync(_game, revision);
            await SchemaRevisionDiffDialog.ShowAsync(
                XamlRoot,
                diff,
                $"Git 差异 · {revision.TitleText} · {revision.ShortCommit}");
        });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SchemaRevisionItem revision } || _game is null)
        {
            return;
        }
        var formatDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"导出修订 {revision.ShortCommit}",
            Content = "选择导出为可直接使用的 BIN，或便于分享的 ZIP。",
            PrimaryButtonText = "导出 ZIP",
            SecondaryButtonText = "导出 BIN",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        var formatResult = await formatDialog.ShowAsync();
        if (formatResult == ContentDialogResult.None)
        {
            return;
        }
        var format = formatResult == ContentDialogResult.Secondary ? "bin" : "zip";
        var extension = format == "bin" ? ".bin" : ".zip";
        string? output;
        try
        {
            output = NativeFilePickerService.PickSaveFile(
                App.WindowHandle,
                "导出 Git 修订",
                $"UserGameStatsSchema_{_game.AppId}{extension}",
                format == "bin" ? "BIN 文件" : "ZIP 压缩文件",
                extension);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法打开保存位置选择器：{exception.Message}", InfoBarSeverity.Error);
            return;
        }
        if (output is null)
        {
            return;
        }
        await RunBusyAsync(async () =>
        {
            await _revisions.ExportAsync(_game, revision, format, output);
            App.ViewModel.ShowInfo($"已导出修订：{output}", InfoBarSeverity.Success);
        });
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SchemaRevisionItem revision }
            || _game is null
            || revision.IsCurrent)
        {
            return;
        }
        await RunBusyAsync(async () =>
        {
            var previews = await _revisions.CompareAsync(_game, revision);
            if (!await ReplacementConfirmationDialog.ShowAsync(
                    XamlRoot,
                    previews,
                    $"比较当前文件与修订 {revision.ShortCommit}",
                    "备份并设为当前"))
            {
                return;
            }
            using var monitoringSuppression = App.ViewModel.Translations
                .BeginSchemaMonitoringSuppression([_game.AppId]);
            await _revisions.ActivateAsync(_game, revision, force: false);
            await App.ViewModel.Translations.ScanAsync(refreshCatalog: false);
            App.ViewModel.ShowInfo("所选修订已设为当前版本，并创建了新的 Git 提交。", InfoBarSeverity.Success);
            await LoadCoreAsync();
        });
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_isBusy)
        {
            return;
        }
        _isBusy = true;
        SetBusyState(true);
        PageLayout.IsHitTestVisible = false;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"修订操作失败：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("Git 修订", exception);
        }
        finally
        {
            PageLayout.IsHitTestVisible = true;
            SetBusyState(false);
            _isBusy = false;
        }
    }

    private void SetBusyState(bool isBusy)
    {
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        LoadingState.IsActive = isBusy;
        RevisionContent.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
    }
}
