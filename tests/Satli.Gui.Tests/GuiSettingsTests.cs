using Satli_Gui.Models;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class GuiSettingsTests
{
    [Fact]
    public void EnablesLogWordWrapByDefault()
    {
        Assert.True(new GuiSettings().LogWordWrap);
    }

    [Fact]
    public void EnablesUpdateChecksByDefault()
    {
        Assert.True(new GuiSettings().CheckForUpdatesOnStartup);
    }
}
