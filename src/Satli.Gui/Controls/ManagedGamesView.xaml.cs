using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Satli_Gui.Models;
using Satli_Gui.Pages;
using Satli_Gui.Services;
using Satli_Gui.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace Satli_Gui.Controls;

public sealed partial class ManagedGamesView : UserControl
{
    private readonly Action<Type, object> _navigate;
    private bool _stateSyncQueued;
    private bool _subscribed;

    public TranslationManagementViewModel ViewModel => App.ViewModel.Translations;
    public ManagedGamesPageState State { get; }

    internal ManagedGamesView(ManagedGameFilter filter, Action<Type, object> navigate)
    {
        _navigate = navigate;
        State = new ManagedGamesPageState(filter, ViewModel.ManagedGames, ViewModel.IsLoading);
        InitializeComponent();
        Loaded += ManagedGamesView_Loaded;
        Unloaded += ManagedGamesView_Unloaded;
        AddShortcut(VirtualKey.A, VirtualKeyModifiers.Control, ToggleSelection_Invoked);
        AddShortcut(VirtualKey.F5, VirtualKeyModifiers.None, Refresh_Invoked);
    }

    private void ManagedGamesView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            return;
        }
        _subscribed = true;
        ViewModel.ManagedGames.CollectionChanged += ManagedGames_CollectionChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        State.Synchronize(ViewModel.ManagedGames, ViewModel.IsLoading);
    }

    private void ManagedGamesView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            return;
        }
        _subscribed = false;
        ViewModel.ManagedGames.CollectionChanged -= ManagedGames_CollectionChanged;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ManagedGames_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueStateSynchronization();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationManagementViewModel.IsLoading))
        {
            QueueStateSynchronization();
        }
    }

    private void QueueStateSynchronization()
    {
        if (_stateSyncQueued)
        {
            return;
        }
        _stateSyncQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _stateSyncQueued = false;
                State.Synchronize(ViewModel.ManagedGames, ViewModel.IsLoading);
            }))
        {
            _stateSyncQueued = false;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanAsync();
        State.Synchronize(ViewModel.ManagedGames, ViewModel.IsLoading);
    }
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
            State.Synchronize(ViewModel.ManagedGames, ViewModel.IsLoading);
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
            _navigate(typeof(AchievementEditorPage), game);
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameItem game })
        {
            _navigate(typeof(RevisionHistoryPage), game);
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
        if (sender is not FrameworkElement { Tag: GameItem game } || !game.CanToggleProtection)
        {
            return;
        }
        var enable = !game.FileReadOnly;
        if (enable && !await SteamFileProtectionDialog.ConfirmLockAsync(XamlRoot, [game]))
        {
            return;
        }
        await ViewModel.SetProtectionAsync([game], enable);
        State.Synchronize(ViewModel.ManagedGames, ViewModel.IsLoading);
    }

    private void ManagedSelection_Click(object sender, RoutedEventArgs e) =>
        State.RefreshSelection();

    private void ToggleSelection_Click(object sender, RoutedEventArgs e) =>
        State.ToggleSelection();

    private async void LockSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = State.SelectedGames
            .Where(item => item.CanToggleProtection && !item.FileReadOnly)
            .ToArray();
        if (selected.Length == 0)
        {
            ViewModel.ShowInfo("请先选择至少一个尚未锁定的已管理游戏。", InfoBarSeverity.Warning);
            return;
        }
        if (await SteamFileProtectionDialog.ConfirmLockAsync(XamlRoot, selected))
        {
            await ViewModel.SetProtectionAsync(selected, enable: true);
            State.Synchronize(ViewModel.ManagedGames, ViewModel.IsLoading);
        }
    }

    private async void UnlockSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = State.SelectedGames
            .Where(item => item.FileReadOnly)
            .ToArray();
        if (selected.Length == 0)
        {
            ViewModel.ShowInfo("请先选择至少一个已锁定的游戏。", InfoBarSeverity.Warning);
            return;
        }
        await ViewModel.SetProtectionAsync(selected, enable: false);
        State.Synchronize(ViewModel.ManagedGames, ViewModel.IsLoading);
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
        State.Synchronize(ViewModel.ManagedGames, ViewModel.IsLoading);
    }

    private void ToggleSelection_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        State.ToggleSelection();
        args.Handled = true;
    }
}
