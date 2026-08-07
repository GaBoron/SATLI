using Satli_Gui.Models;
using Satli_Gui.Services;
using Satli_Gui.ViewModels;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class GameInstallFilteringTests
{
    [Fact]
    public void SharedFiltersUseStableOrderAndLabels()
    {
        Assert.Equal(
            ["全部", "未安装", "已安装", "可更新", "需处理"],
            GameInstallFiltering.Options.Select(item => item.Label));
    }

    [Fact]
    public void InventoryScopesExposeOnlyMeaningfulFilters()
    {
        Assert.Equal(
            ["全部", "未安装", "已安装", "需处理"],
            GameInstallFiltering.OptionsFor(GameInventoryScope.Local).Select(item => item.Label));
        Assert.Equal(
            ["未安装", "已安装"],
            GameInstallFiltering.OptionsFor(GameInventoryScope.Cloud).Select(item => item.Label));
    }

    [Theory]
    [InlineData("installed", true)]
    [InlineData("modified", true)]
    [InlineData("unmanaged", false)]
    [InlineData("restored", false)]
    [InlineData("missing", false)]
    [InlineData("unreadable", false)]
    public void CloudFiltersPartitionEveryEntryByInstalledAvailability(string state, bool installed)
    {
        var game = CatalogGame();
        game.InstalledState = state;

        Assert.Equal(
            installed,
            GameInstallFiltering.Matches(game, GameInstallFilter.Installed, GameInventoryScope.Cloud));
        Assert.Equal(
            !installed,
            GameInstallFiltering.Matches(game, GameInstallFilter.Uninstalled, GameInventoryScope.Cloud));
    }

    [Fact]
    public void CatalogInstallIsUpdateOnlyWhenSameVariantHashChanged()
    {
        var game = CatalogGame();
        game.InstalledSha256 = "old-hash";

        Assert.True(game.IsUpdateAvailable);
        Assert.True(GameInstallFiltering.Matches(game, GameInstallFilter.UpdateAvailable));

        game.InstalledSha256 = "new-hash";
        Assert.False(game.IsUpdateAvailable);
    }

    [Theory]
    [InlineData("local-edit", "installed")]
    [InlineData("local-import", "installed")]
    [InlineData("catalog", "modified")]
    [InlineData("catalog", "missing")]
    [InlineData("catalog", "unreadable")]
    public void LocalOrExceptionalStatesAreNotUpdates(string source, string state)
    {
        var game = CatalogGame();
        game.InstalledSource = source;
        game.InstalledState = state;
        game.InstalledSha256 = "old-hash";

        Assert.False(game.IsUpdateAvailable);
        Assert.Equal(
            state is "modified" or "missing" or "unreadable",
            GameInstallFiltering.Matches(game, GameInstallFilter.Attention));
    }

    [Fact]
    public void DeletedCatalogVariantRequiresAttentionInsteadOfUpdate()
    {
        var game = CatalogGame();
        game.InstalledVariantId = "removed";
        game.InstalledSha256 = "old-hash";

        Assert.False(game.IsUpdateAvailable);
        Assert.True(game.NeedsAttention);
    }

    [Fact]
    public void StatusFilterClearsSelectionWhileSearchPreservesIt()
    {
        var viewModel = new TranslationManagementViewModel(
            () => new GuiSettings(),
            new ApplicationOperationState(),
            (_, _) => { });
        var game = CatalogGame();
        game.IsSelected = true;
        viewModel.Games.Add(game);

        viewModel.SearchText = "Test";
        Assert.True(game.IsSelected);

        viewModel.SelectedFilterOption = GameInstallFiltering.UpdateOption;
        Assert.False(game.IsSelected);
        Assert.Equal(GameInstallFilter.UpdateAvailable, viewModel.SelectedFilterOption.Value);
    }

    private static GameItem CatalogGame()
    {
        var game = new GameItem
        {
            AppId = "123",
            GameName = "Test Game",
            InstalledState = "installed",
            InstalledSource = "catalog",
            InstalledVariantId = "default",
            InstalledSha256 = "new-hash",
        };
        game.Variants.Add(new SchemaVariantOption
        {
            VariantId = "default",
            Primary = true,
            Sha256 = "new-hash",
        });
        return game;
    }
}
