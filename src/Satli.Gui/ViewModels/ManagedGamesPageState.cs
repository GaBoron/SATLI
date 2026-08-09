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
    public string Title => Filter == ManagedGameFilter.Locked ? "已锁定" : "全部已管理";
    public string Description => Filter == ManagedGameFilter.Locked
        ? "管理已强制设为只读的完整 Steam 成就 schema，并可随时解除锁定。"
        : "查看社区安装、本地导入或本地编辑的当前译文，并恢复变更前文件。";
    public string EmptyTitle => Filter == ManagedGameFilter.Locked
        ? "暂无已锁定的成就文件"
        : "暂无已管理的游戏";
    public string EmptyDescription => Filter == ManagedGameFilter.Locked
        ? "在“全部”页面选择项目并强制锁定后，会显示在这里。"
        : "安装、导入或保存本地编辑后会显示在这里。";
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"已选 {SelectedCount} 项";
    public string SelectionActionText =>
        Items.Count > 0 && Items.All(item => item.IsSelected) ? "取消全选" : "全选";
    public Visibility LockActionVisibility => Filter == ManagedGameFilter.All
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
