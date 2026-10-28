using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Satli_Gui.Models;

namespace Satli_Gui.ViewModels;

public sealed class ManagedGameRow(GameItem game) : ObservableObject
{
    private bool _isSelected;

    public GameItem Game { get; } = game;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ManagedGamesPageState : ObservableObject
{
    private bool _isLoading;

    public ManagedGamesPageState(
        ManagedGameFilter filter,
        IEnumerable<GameItem> source,
        bool isLoading)
    {
        Filter = filter;
        _isLoading = isLoading;
        Synchronize(source, isLoading);
    }

    public ManagedGameFilter Filter { get; }
    public ObservableCollection<ManagedGameRow> Items { get; } = [];
    public string Title => Filter switch
    {
        ManagedGameFilter.Modified => "被修改",
        ManagedGameFilter.Locked => "SATLI-lite 译文",
        _ => "全部已管理",
    };
    public string Description => Filter switch
    {
        ManagedGameFilter.Modified => "BIN 可能被 Steam 或其他程序修改。可选用可信快照发送到 SATLI-lite，通过独立 JSON 管理显示；也可恢复 BIN。",
        ManagedGameFilter.Locked => "显示经公开接口查询或导入的 lite 译文。可刷新状态；游戏和全局显示开关请在 Steam 的 SATLI-lite 中管理。",
        _ => "分别查看 BIN 安装记录与 lite 返回的译文状态。JSON 安装不创建 BIN 记录；只有 BIN 记录可恢复文件。",
    };
    public string EmptyTitle => Filter switch
    {
        ManagedGameFilter.Modified => "没有被修改的游戏",
        ManagedGameFilter.Locked => "暂无已查询或导入的 lite 译文",
        _ => "暂无已管理的游戏",
    };
    public string EmptyDescription => Filter switch
    {
        ManagedGameFilter.Modified => "SATLI 检测到已管理文件被替换后，会自动显示在这里。",
        ManagedGameFilter.Locked => "从云端游戏直接应用 JSON 或查询 lite 状态，无需先安装 BIN。",
        _ => "安装、导入或保存本地编辑后会显示在这里。",
    };
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"已选 {SelectedCount} 项";
    public string SelectionActionText =>
        Items.Count > 0 && Items.All(item => item.IsSelected) ? "取消全选" : "全选";
    public Visibility LockActionVisibility => Filter is ManagedGameFilter.All or ManagedGameFilter.Modified
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool IsLoading => _isLoading;
    public Visibility ListVisibility => IsLoading ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EmptyVisibility => !IsLoading && Items.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public IReadOnlyList<GameItem> SelectedGames => Items
        .Where(item => item.IsSelected)
        .Select(item => item.Game)
        .ToArray();

    public void Synchronize(IEnumerable<GameItem> source, bool isLoading)
    {
        var filtered = source
            .Where(item => ManagedGameFiltering.Matches(item, Filter))
            .ToArray();
        var collectionChanged = Items.Count != filtered.Length
            || Items.Where((item, index) => !ReferenceEquals(item.Game, filtered[index])).Any();
        if (collectionChanged)
        {
            var selectedAppIds = Items
                .Where(item => item.IsSelected)
                .Select(item => item.Game.AppId)
                .ToHashSet(StringComparer.Ordinal);
            Items.Clear();
            foreach (var game in filtered)
            {
                Items.Add(new ManagedGameRow(game)
                {
                    IsSelected = selectedAppIds.Contains(game.AppId),
                });
            }
        }
        var loadingChanged = SetProperty(ref _isLoading, isLoading, nameof(IsLoading));
        if (collectionChanged || loadingChanged)
        {
            OnPropertyChanged(nameof(ListVisibility));
            OnPropertyChanged(nameof(EmptyVisibility));
            RefreshSelection();
        }
    }

    public void ToggleSelection()
    {
        var selectAll = Items.Any(item => !item.IsSelected);
        foreach (var item in Items)
        {
            item.IsSelected = selectAll;
        }
        RefreshSelection();
    }

    public void RefreshSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(SelectionActionText));
        OnPropertyChanged(nameof(SelectedGames));
    }
}
