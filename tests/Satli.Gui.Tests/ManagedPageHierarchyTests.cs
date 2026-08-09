using Satli_Gui.Controls;
using Satli_Gui.Pages;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class ManagedPageHierarchyTests
{
    [Fact]
    public void ManagedNavigationDestinationsAreDistinctPagesWithSharedContent()
    {
        Assert.NotEqual(typeof(ManagedAllPage), typeof(ManagedLockedPage));
        Assert.True(typeof(Page).IsAssignableFrom(typeof(ManagedAllPage)));
        Assert.True(typeof(Page).IsAssignableFrom(typeof(ManagedLockedPage)));
        Assert.Equal(typeof(Page), typeof(ManagedAllPage).BaseType);
        Assert.Equal(typeof(Page), typeof(ManagedLockedPage).BaseType);
        Assert.True(typeof(UserControl).IsAssignableFrom(typeof(ManagedGamesView)));
        Assert.False(typeof(ManagedGamesView).IsAssignableFrom(typeof(ManagedAllPage)));
        Assert.False(typeof(ManagedGamesView).IsAssignableFrom(typeof(ManagedLockedPage)));
        Assert.NotNull(typeof(ManagedAllPage).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(ManagedLockedPage).GetConstructor(Type.EmptyTypes));
    }
}
