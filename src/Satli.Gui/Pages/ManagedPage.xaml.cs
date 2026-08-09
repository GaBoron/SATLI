using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Satli_Gui.Models;
using Satli_Gui.Services;
using Satli_Gui.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace Satli_Gui.Pages;

public sealed partial class ManagedPage : Page
{
    public TranslationManagementViewModel ViewModel => App.ViewModel.Translations;
    public ManagedPage()
    {
        InitializeComponent();
        AddShortcut(VirtualKey.F5, VirtualKeyModifiers.None, Refresh_Invoked);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SetManagedFilter(
            e.Parameter is ManagedGameFilter filter ? filter : ManagedGameFilter.All);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanAsync();
    private async Task ConfirmRestoreAsync(IReadOnlyList<GameItem> selected, bool force)
    {
        var previews = await ViewModel.PreviewRestoreAsync(selected, force);
        if (previews is null)
        {
            return;
        }
        if (await ReplacementConfirmationDialog.ShowAsync(
                XamlRoot,
                previews,
                force
                    ? $"确认强制恢复 {selected.Count} 个游戏并归档当前文件"
                    : $"确认恢复 {selected.Count} 个游戏",
                force ? "确认归档并恢复" : "确认恢复"))
        {
            await ViewModel.RestoreAsync(selected, force);
        }
    }

    private async void ViewCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameItem game } || !game.CanViewInstalledTranslation)
        {
            return;
        }
        var preview = await ViewModel.PreviewCurrentAsync(game);
        if (preview is not null)
        {
            await ReplacementConfirmationDialog.ShowReadOnlyAsync(
                XamlRoot,
                [preview],
                $"查看当前翻译 · {game.GameName}");
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameItem game } && game.CanViewInstalledTranslation)
        {
            Frame.Navigate(typeof(AchievementEditorPage), game);
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameItem game })
        {
            Frame.Navigate(typeof(RevisionHistoryPage), game);
        }
    }

    private async void RestoreItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameItem game } && game.CanRestore)
        {
            await ConfirmRestoreAsync([game], force: game.RequiresForceRestore);
        }
    }

    private async void Protection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameItem { FileReadOnly: true } game })
        {
            return;
        }
        await ViewModel.SetProtectionAsync([game], enable: false);
    }

    private void AddShortcut(
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += handler;
        KeyboardAccelerators.Add(accelerator);
    }

    private async void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.ScanAsync();
    }
}
