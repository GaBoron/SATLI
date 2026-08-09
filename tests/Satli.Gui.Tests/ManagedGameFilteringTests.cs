using Satli_Gui.Models;
using Satli_Gui.ViewModels;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class ManagedGameFilteringTests
{
    [Fact]
    public void LockedFilterOnlyMatchesReadOnlySchemas()
    {
        var locked = new GameItem { AppId = "123", GameName = "Locked", FileReadOnly = true };
        var unlocked = new GameItem { AppId = "456", GameName = "Unlocked" };

        Assert.True(ManagedGameFiltering.Matches(locked, ManagedGameFilter.Locked));
        Assert.False(ManagedGameFiltering.Matches(unlocked, ManagedGameFilter.Locked));
        Assert.True(ManagedGameFiltering.Matches(unlocked, ManagedGameFilter.All));
    }

    [Fact]
    public void PageStatesKeepAllAndLockedCollectionsIndependent()
    {
        var source = CreateManagedGames();
        var all = new ManagedGamesPageState(ManagedGameFilter.All, source, isLoading: false);
        var locked = new ManagedGamesPageState(ManagedGameFilter.Locked, source, isLoading: false);

        Assert.Equal(2, all.Items.Count);
        Assert.Equal("全部已管理", all.Title);
        Assert.Single(locked.Items);
        Assert.Equal("123", locked.Items[0].Game.AppId);
        Assert.Equal("已锁定", locked.Title);

        locked.ToggleSelection();
        Assert.Equal(1, locked.SelectedCount);
        Assert.Equal(0, all.SelectedCount);
    }

    [Fact]
    public void SynchronizingUnchangedStateDoesNotRebuildCollectionOrReplayAnimations()
    {
        var source = CreateManagedGames();
        var state = new ManagedGamesPageState(ManagedGameFilter.Locked, source, isLoading: false);
        var collectionChanges = 0;
        state.Items.CollectionChanged += (_, _) => collectionChanges++;

        state.Synchronize(source, isLoading: false);

        Assert.Equal(0, collectionChanges);
        Assert.Single(state.Items);
    }

    [Fact]
    public void PageSelectionTogglesOnlyItsOwnVisibleItems()
    {
        var state = new ManagedGamesPageState(
            ManagedGameFilter.Locked,
            CreateManagedGames(),
            isLoading: false);

        state.ToggleSelection();
        Assert.Equal(1, state.SelectedCount);
        Assert.Equal("已选 1 项", state.SelectedCountText);

        state.ToggleSelection();
        Assert.Equal(0, state.SelectedCount);
        Assert.All(state.Items, item => Assert.False(item.IsSelected));
    }

    private static GameItem[] CreateManagedGames() =>
        [
            new GameItem { AppId = "123", GameName = "Locked", FileReadOnly = true },
            new GameItem { AppId = "456", GameName = "Unlocked" },
        ];
}
