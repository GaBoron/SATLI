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
    public void ViewModelSwitchesBetweenAllAndLockedCollections()
    {
        var viewModel = new TranslationManagementViewModel(
            () => new GuiSettings(),
            new ApplicationOperationState(),
            (_, _) => { });
        viewModel.ManagedGames.Add(
            new GameItem { AppId = "123", GameName = "Locked", FileReadOnly = true });
        viewModel.ManagedGames.Add(
            new GameItem { AppId = "456", GameName = "Unlocked" });

        Assert.True(viewModel.SetManagedFilter(ManagedGameFilter.Locked));
        Assert.Single(viewModel.VisibleManagedGames);
        Assert.Equal("123", viewModel.VisibleManagedGames[0].AppId);
        Assert.Equal("已锁定", viewModel.ManagedPageTitle);

        Assert.True(viewModel.SetManagedFilter(ManagedGameFilter.All));
        Assert.Equal(2, viewModel.VisibleManagedGames.Count);
        Assert.Equal("全部已管理", viewModel.ManagedPageTitle);
    }

    [Fact]
    public void ReSelectingCurrentFilterDoesNotRebuildCollectionOrReplayAnimations()
    {
        var viewModel = CreateViewModelWithManagedGames();
        Assert.True(viewModel.SetManagedFilter(ManagedGameFilter.Locked));
        var collectionChanges = 0;
        viewModel.VisibleManagedGames.CollectionChanged += (_, _) => collectionChanges++;

        Assert.False(viewModel.SetManagedFilter(ManagedGameFilter.Locked));

        Assert.Equal(0, collectionChanges);
        Assert.Single(viewModel.VisibleManagedGames);
    }

    [Fact]
    public void ManagedSelectionTogglesVisibleItemsAndClearsWhenFilterChanges()
    {
        var viewModel = CreateViewModelWithManagedGames();
        viewModel.SetManagedFilter(ManagedGameFilter.Locked);

        viewModel.ToggleVisibleManagedSelection();
        Assert.Equal(1, viewModel.ManagedSelectedCount);
        Assert.Equal("已选 1 项", viewModel.ManagedSelectedCountText);

        viewModel.SetManagedFilter(ManagedGameFilter.All);
        Assert.Equal(0, viewModel.ManagedSelectedCount);
        Assert.All(viewModel.VisibleManagedGames, item => Assert.False(item.IsSelected));
    }

    private static TranslationManagementViewModel CreateViewModelWithManagedGames()
    {
        var viewModel = new TranslationManagementViewModel(
            () => new GuiSettings(),
            new ApplicationOperationState(),
            (_, _) => { });
        viewModel.ManagedGames.Add(
            new GameItem { AppId = "123", GameName = "Locked", FileReadOnly = true });
        viewModel.ManagedGames.Add(
            new GameItem { AppId = "456", GameName = "Unlocked" });
        return viewModel;
    }
}
