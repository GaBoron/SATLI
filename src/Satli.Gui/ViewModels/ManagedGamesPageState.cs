using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Satli_Gui.Models;

namespace Satli_Gui.ViewModels;

public sealed class ManagedGameRow : ObservableObject, IDisposable
{
    private bool _isSelected;

    public GameItem Game { get; }
    private readonly bool _json;
    public ManagedGameRow(GameItem game, bool json)
    {
        Game = game;
        _json = json;
        Game.PropertyChanged += Changed;
    }
    public string Status => _json ? (Game.LitePending ? "等待处理" : Game.LiteEnabled ? "已启用" : "已停用") : Game.StateText;
    public string Summary => _json ? $"版本：{Game.LiteVariantId} · 语言：{Game.LiteLanguage}" : Game.ManagedSummaryText;
    public string Hint => _json ? Game.LiteStateText : Game.IsModified ? "文件与安装时不同，可恢复原文件或改用 JSON 显示。" : "";
    public Visibility HintVisibility => Hint.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility BinVisibility => _json ? Visibility.Collapsed : Visibility.Visible;
    public Visibility JsonVisibility => _json ? Visibility.Visible : Visibility.Collapsed;
    public bool CanEdit => _json ? Game.LiteInstalled && Game.LiteReadable : Game.CanViewInstalledTranslation;
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        foreach (var name in new[] { nameof(Status), nameof(Summary), nameof(Hint), nameof(HintVisibility), nameof(CanEdit) }) OnPropertyChanged(name);
    }
    public void Dispose() => Game.PropertyChanged -= Changed;

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
    public string Title => Filter == ManagedGameFilter.Json ? "JSON 译文" : "BIN 译文";
    public string Description => Filter == ManagedGameFilter.Json ? "读取 SATLI-lite 已保存的译文，打开即可校对和修改。" : "管理本机 BIN 译文、备份和恢复。";
    public string EmptyTitle => Filter == ManagedGameFilter.Json ? "暂无已读取的 JSON 译文" : "暂无 BIN 管理记录";
    public string EmptyDescription => Filter == ManagedGameFilter.Json ? "刷新以读取 lite 清单；旧版插件需要更新后才能提供公开快照。" : "安装或导入 BIN 后会显示在这里。";
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"已选 {SelectedCount} 项";
    public string SelectionActionText =>
        Items.Count > 0 && Items.All(item => item.IsSelected) ? "取消全选" : "全选";
    public Visibility LockActionVisibility => Filter == ManagedGameFilter.Bin
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility JsonVisibility => Filter == ManagedGameFilter.Json ? Visibility.Visible : Visibility.Collapsed;
    public void Release()
    {
        foreach (var row in Items) row.Dispose();
        Items.Clear();
    }
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
            foreach (var row in Items) row.Dispose();
            Items.Clear();
            foreach (var game in filtered)
            {
                Items.Add(new ManagedGameRow(game, Filter == ManagedGameFilter.Json)
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
