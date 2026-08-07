using Satli_Gui.Models;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class GameSelectionOperationsTests
{
    [Fact]
    public void ToggleSelectsThenClearsTheCurrentResult()
    {
        var games = new[]
        {
            new GameItem { AppId = "1", GameName = "One" },
            new GameItem { AppId = "2", GameName = "Two" },
        };

        GameSelectionOperations.ToggleVisible(games);
        Assert.All(games, game => Assert.True(game.IsSelected));
        Assert.True(GameSelectionOperations.AreAllSelected(games));

        GameSelectionOperations.ToggleVisible(games);
        Assert.All(games, game => Assert.False(game.IsSelected));
        Assert.False(GameSelectionOperations.AreAllSelected(games));
        Assert.False(GameSelectionOperations.AreAllSelected([]));
    }
}
