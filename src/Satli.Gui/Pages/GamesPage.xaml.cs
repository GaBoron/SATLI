using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Satli_Gui.Models;
using Satli_Gui.Services;
using Satli_Gui.ViewModels;
using Windows.System;
using Windows.UI.Core;
using Windows.Foundation;

namespace Satli_Gui.Pages;

public sealed partial class GamesPage : Page
{
    private readonly TranslationUpdateDiffService _updateDiffs = new();
    private int? _selectionAnchorIndex;

    public TranslationManagementViewModel ViewModel => App.ViewModel.Translations;

    public GamesPage()
    {
        InitializeComponent();
        AddShortcut(VirtualKey.A, VirtualKeyModifiers.Control, ToggleSelection_Invoked);
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control, FocusSearch_Invoked);
        AddShortcut(VirtualKey.F5, VirtualKeyModifiers.None, Refresh_Invoked);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanAsync();
    private void ToggleSelection_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleVisibleSelection();
        _selectionAnchorIndex = null;
    }

    private async void Petition_Click(object sender, RoutedEventArgs e) =>
        await TranslationPetitionDialogService.RunAsync(XamlRoot);

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.Games.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ViewModel.ShowInfo("请先选择至少一个游戏。", InfoBarSeverity.Warning);
            return;
        }

        var previews = await ViewModel.PreviewInstallAsync(selected);
        if (previews is null)
        {
            return;
        }

        IReadOnlyList<TranslationUpdateDiff>? updateDiffs;
        try
        {
            updateDiffs = await _updateDiffs.CreateAsync(
                selected,
                previews,
                ViewModel.PreviewCurrentAsync);
        }
        catch (Exception exception)
        {
            ViewModel.ShowInfo($"无法生成更新差异：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("更新差异", exception);
            return;
        }
        if (updateDiffs is null)
        {
            return;
        }

        var updateAppIds = updateDiffs
            .Select(update => update.Game.AppId)
            .ToHashSet(StringComparer.Ordinal);
        var remainingPreviews = previews
            .Where(preview => !updateAppIds.Contains(preview.AppId))
            .ToArray();
        for (var index = 0; index < updateDiffs.Count; index++)
        {
            var update = updateDiffs[index];
            var title = updateDiffs.Count == 1
                ? $"确认更新 · {update.Game.GameName}"
                : $"确认更新 {index + 1}/{updateDiffs.Count} · {update.Game.GameName}";
            var confirmText = index + 1 == updateDiffs.Count && remainingPreviews.Length == 0
                ? "确认更新"
                : "确认并继续";
            if (!await SchemaRevisionDiffDialog.ConfirmAsync(
                    XamlRoot,
                    update.Diff,
                    title,
                    "当前已安装版本",
                    confirmText))
            {
                return;
            }
        }

        if (remainingPreviews.Length > 0)
        {
            var remainingGames = selected
                .Where(game => !updateAppIds.Contains(game.AppId))
                .ToArray();
            var outdated = remainingGames.Count(item => !item.IsCurrent);
            var title = outdated == 0
                ? $"确认安装 {remainingPreviews.Length} 个翻译"
                : $"确认安装 {remainingPreviews.Length} 个翻译（{outdated} 个可能已过期）";
            if (!await ReplacementConfirmationDialog.ShowAsync(
                    XamlRoot,
                    remainingPreviews,
                    title,
                    "确认安装"))
            {
                return;
            }
        }

        await ViewModel.InstallAsync(selected);
    }

    private void GameSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not GameItem item)
        {
            return;
        }
        var index = ViewModel.VisibleGames.IndexOf(item);
        if (index < 0)
        {
            return;
        }
        var shiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (shiftPressed
            && _selectionAnchorIndex is int anchor
            && anchor >= 0
            && anchor < ViewModel.VisibleGames.Count)
        {
            var selected = checkBox.IsChecked == true;
            for (var position = Math.Min(anchor, index); position <= Math.Max(anchor, index); position++)
            {
                ViewModel.VisibleGames[position].IsSelected = selected;
            }
        }
        _selectionAnchorIndex = index;
        ViewModel.RefreshSelectionCount();
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

    private void ToggleSelection_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or AutoSuggestBox)
        {
            return;
        }
        ViewModel.ToggleVisibleSelection();
        _selectionAnchorIndex = null;
        args.Handled = true;
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private async void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.ScanAsync();
    }

}
