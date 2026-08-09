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

        viewModel.SetManagedFilter(ManagedGameFilter.All);
        Assert.Equal(2, viewModel.VisibleManagedGames.Count);
        Assert.Equal("全部已管理", viewModel.ManagedPageTitle);

        viewModel.SetManagedFilter(ManagedGameFilter.Locked);
        Assert.Single(viewModel.VisibleManagedGames);
        Assert.Equal("123", viewModel.VisibleManagedGames[0].AppId);
        Assert.Equal("已锁定", viewModel.ManagedPageTitle);
    }
}
