using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Satl_Gui.Pages;

public sealed partial class AchievementEditorPage
{
    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveDraftCheckpointAsync())
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
        if (!await SaveDraftCheckpointAsync())
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
        if (!await SaveDraftCheckpointAsync())
        {
            return;
        }
        _allowClose = true;
        App.Window.Close();
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
        SetBusyState(true);
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
            SetBusyState(false);
            _isBusy = false;
        }
    }

    private void SetBusyState(bool isBusy)
    {
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        LoadingState.IsActive = isBusy;
        EditorContent.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
    }
}
