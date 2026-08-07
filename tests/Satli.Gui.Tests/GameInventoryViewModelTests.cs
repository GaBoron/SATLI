using Microsoft.UI.Xaml;
using Satli_Gui.Models;
using Satli_Gui.ViewModels;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class GameInventoryViewModelTests
{
    [Theory]
    [InlineData(GameInventoryScope.Local)]
    [InlineData(GameInventoryScope.Cloud)]
    public void InventoryStartsWithLoadingPresentation(GameInventoryScope scope)
    {
        var viewModel = new GameInventoryViewModel(scope);

        Assert.True(viewModel.IsLoading);
        Assert.Equal(Visibility.Collapsed, viewModel.GameListVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.EmptyStateVisibility);
    }
}
