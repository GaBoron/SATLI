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
        ManagedGameFilter.Locked => "已锁定",
        _ => "全部已管理",
    };
    public string Description => Filter switch
    {
        ManagedGameFilter.Modified => "集中处理被 Steam 更新覆盖的游戏，可直接锁定显示、查看当前文件或恢复。",
        ManagedGameFilter.Locked => "管理由 Millennium 插件覆盖显示的 Steam 成就译文，并可随时解除。",
        _ => "查看社区安装、本地导入或本地编辑的当前译文，并恢复变更前文件。",
    };
    public string EmptyTitle => Filter switch
    {
        ManagedGameFilter.Modified => "没有被修改的游戏",
        ManagedGameFilter.Locked => "暂无已锁定的 Steam 显示译文",
        _ => "暂无已管理的游戏",
    };
    public string EmptyDescription => Filter switch
    {
        ManagedGameFilter.Modified => "SATLI 检测到已管理文件被替换后，会自动显示在这里。",
        ManagedGameFilter.Locked => "在“全部”或“被修改”页面锁定 Steam 显示后，会显示在这里。",
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
