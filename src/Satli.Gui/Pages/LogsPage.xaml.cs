using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Satli_Gui.Models;

namespace Satli_Gui.Pages;

public sealed partial class LogsPage : Page
{
    private IReadOnlyList<LogEntryPresentation> _allEntries = [];
    public ObservableCollection<LogEntryPresentation> VisibleEntries { get; } = [];

    public LogsPage()
    {
        InitializeComponent();
        Loaded += LogsPage_Loaded;
    }

    private void LogsPage_Loaded(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () => _ = RefreshAsync());
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清理全部日志？",
            Content = "这会把本机日志目录中的 SATLI GUI 日志移入回收站。",
            PrimaryButtonText = "清理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await App.Logs.ClearAsync();
            _allEntries = [];
            ApplyFilter();
            App.ViewModel.ShowInfo("日志已清理。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法清理日志：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ApplyFilter();
        }
    }

    private async Task RefreshAsync()
    {
        SetLoading(true);
        try
        {
            var wrapping = App.ViewModel.Settings.LogWordWrap
                ? TextWrapping.Wrap
                : TextWrapping.NoWrap;
            _allEntries = LogEntryParser.Parse(await App.Logs.ReadRecentAsync())
                .Reverse()
                .ToArray();
            foreach (var entry in _allEntries)
            {
                entry.MessageWrapping = wrapping;
            }
            ApplyFilter();
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法读取日志：{exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool isLoading)
    {
        LoadingState.IsActive = isLoading;
        LogList.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        VisibleEntries.Clear();
        foreach (var entry in _allEntries.Where(entry => entry.Matches(query)))
        {
            VisibleEntries.Add(entry);
        }
        EmptyState.Visibility = VisibleEntries.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (VisibleEntries.Count > 0)
        {
            LogList.ScrollIntoView(
                VisibleEntries[0],
                ScrollIntoViewAlignment.Leading);
        }
    }
}
