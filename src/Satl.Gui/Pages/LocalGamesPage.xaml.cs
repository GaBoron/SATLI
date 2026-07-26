using Microsoft.Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Satl_Gui.ViewModels;
using Windows.System;

namespace Satl_Gui.Pages;

public sealed partial class LocalGamesPage : Page
{
    private static readonly Uri LocalizerUri = new(
        "https://github.com/GaBoron/steam-achievement-localizer-skill");
    private readonly LocalImportService _localImport = new();
    private bool _isImporting;

    public GameInventoryViewModel ViewModel { get; } = new(GameInventoryScope.Local);

    public LocalGamesPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += LocalGamesPage_Loaded;
    }

    private async void LocalGamesPage_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.InitializeAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();

    private async void Localizer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(LocalizerUri))
            {
                App.ViewModel.ShowInfo("无法打开 Steam Achievement Localizer Skill 页面。", InfoBarSeverity.Error);
            }
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法打开 Localizer Skill 页面：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_isImporting)
        {
            return;
        }
        _isImporting = true;
        if (sender is AppBarButton importButton)
        {
            importButton.IsEnabled = false;
        }
        try
        {
            var sourcePath = await PickImportSourceAsync();
            if (sourcePath is null)
            {
                return;
            }
            var preview = await _localImport.PreviewAsync(sourcePath, App.ViewModel.Settings);
            if (!await ReplacementConfirmationDialog.ShowAsync(
                    XamlRoot,
                    [preview.Replacement],
                    "确认导入本地翻译",
                    "导入并安装"))
            {
                return;
            }
            await _localImport.InstallAsync(
                sourcePath,
                preview.SchemaSha256,
                App.ViewModel.Settings);
            await ViewModel.RefreshAsync();
            await App.ViewModel.Translations.ScanAsync(refreshCatalog: false);
            App.ViewModel.ShowInfo(
                $"已导入并安装 {preview.Replacement.GameName}（App ID {preview.Replacement.AppId}）。",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"本地导入失败：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteAsync("错误", "本地导入", exception.ToString());
        }
        finally
        {
            _isImporting = false;
            if (sender is AppBarButton completedButton)
            {
                completedButton.IsEnabled = true;
            }
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameItem game })
        {
            Frame.Navigate(typeof(AchievementEditorPage), game);
        }
    }

    private async Task<string?> PickImportSourceAsync()
    {
        try
        {
            return await App.DispatcherQueue.EnqueueAsync(async () =>
            {
                var picker = new FileOpenPicker(App.Window.AppWindow.Id)
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    CommitButtonText = "导入",
                    SettingsIdentifier = "LocalSchemaImportPicker",
                    Title = "选择 Localizer Skill 生成的 BIN 或 ZIP",
                };
                picker.FileTypeFilter.Add(".bin");
                picker.FileTypeFilter.Add(".zip");
                return (await picker.PickSingleFileAsync())?.Path;
            });
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法打开本地导入文件选择器：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteAsync("错误", "文件选择器", exception.ToString());
            return null;
        }
    }
}
