using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Satli_Gui.Models;

namespace Satli_Gui.ViewModels;

public sealed partial class TranslationManagementViewModel
{
    private ManagedGameFilter _managedFilter;

    public ObservableCollection<GameItem> VisibleManagedGames { get; } = [];
    public ManagedGameFilter ManagedFilter => _managedFilter;
    public string ManagedPageTitle => _managedFilter == ManagedGameFilter.Locked ? "已锁定" : "全部已管理";
    public string ManagedPageDescription => _managedFilter == ManagedGameFilter.Locked
        ? "管理已强制设为只读的完整 Steam 成就 schema，并可随时解除锁定。"
        : "查看社区安装、本地导入或本地编辑的当前译文，并恢复变更前文件。";
    public string ManagedEmptyTitle => _managedFilter == ManagedGameFilter.Locked
        ? "暂无已锁定的成就文件"
        : "暂无已管理的游戏";
    public string ManagedEmptyDescription => _managedFilter == ManagedGameFilter.Locked
        ? "在“全部”页面选择项目并强制锁定后，会显示在这里。"
        : "安装、导入或保存本地编辑后会显示在这里。";
    public int ManagedSelectedCount => VisibleManagedGames.Count(item => item.IsSelected);
    public string ManagedSelectedCountText => $"已选 {ManagedSelectedCount} 项";
    public string ManagedSelectionActionText =>
        GameSelectionOperations.AreAllSelected(VisibleManagedGames) ? "取消全选" : "全选";
    public Visibility ManagedLockActionVisibility => _managedFilter == ManagedGameFilter.All
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool SetManagedFilter(ManagedGameFilter filter)
    {
        if (_managedFilter == filter)
        {
            return false;
        }
        _managedFilter = filter;
        OnPropertyChanged(nameof(ManagedFilter));
        OnPropertyChanged(nameof(ManagedPageTitle));
        OnPropertyChanged(nameof(ManagedPageDescription));
        OnPropertyChanged(nameof(ManagedEmptyTitle));
        OnPropertyChanged(nameof(ManagedEmptyDescription));
        OnPropertyChanged(nameof(ManagedLockActionVisibility));
        ApplyManagedFilter();
        return true;
    }

    public void ToggleVisibleManagedSelection()
    {
        GameSelectionOperations.ToggleVisible(VisibleManagedGames);
        RefreshManagedSelectionCount();
    }

    public void RefreshManagedSelectionCount()
    {
        OnPropertyChanged(nameof(ManagedSelectedCount));
        OnPropertyChanged(nameof(ManagedSelectedCountText));
        OnPropertyChanged(nameof(ManagedSelectionActionText));
    }

    private void ApplyManagedFilter()
    {
        GameSelectionOperations.ClearAll(ManagedGames);
        VisibleManagedGames.Clear();
        foreach (var game in ManagedGames.Where(item => ManagedGameFiltering.Matches(item, _managedFilter)))
        {
            VisibleManagedGames.Add(game);
        }
        OnPropertyChanged(nameof(ManagedEmptyStateVisibility));
        RefreshManagedSelectionCount();
    }
}
