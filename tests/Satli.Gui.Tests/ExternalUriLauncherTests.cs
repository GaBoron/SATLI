using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class ExternalUriLauncherTests
{
    [Fact]
    public async Task WindowsLauncherIsUsedFirst()
    {
        var windowsCalls = 0;
        var shellCalls = 0;
        var launcher = new ExternalUriLauncher(
            _ =>
            {
                windowsCalls++;
                return Task.FromResult(true);
            },
            _ =>
            {
                shellCalls++;
                return true;
            });

        var opened = await launcher.LaunchAsync(new Uri("https://example.com"));

        Assert.True(opened);
        Assert.Equal(1, windowsCalls);
        Assert.Equal(0, shellCalls);
    }

    [Fact]
    public async Task WebLinkFallsBackToShellWhenWindowsLauncherFails()
    {
        var shellCalls = 0;
        var launcher = new ExternalUriLauncher(
            _ => Task.FromResult(false),
            _ =>
            {
                shellCalls++;
                return true;
            });

        var opened = await launcher.LaunchAsync(new Uri("https://example.com"));

        Assert.True(opened);
        Assert.Equal(1, shellCalls);
    }
}
