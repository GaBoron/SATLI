using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class ApplicationDistributionServiceTests
{
    [Fact]
    public void PackagedBuildUsesStoreManagedUpdates()
    {
        var service = new ApplicationDistributionService(() => true);

        Assert.Equal(ApplicationDistributionChannel.MicrosoftStore, service.Channel);
        Assert.True(service.UsesStoreManagedUpdates);
    }

    [Fact]
    public void UnpackagedBuildKeepsGitHubManagedUpdates()
    {
        var service = new ApplicationDistributionService(() => false);

        Assert.Equal(ApplicationDistributionChannel.Standalone, service.Channel);
        Assert.False(service.UsesStoreManagedUpdates);
    }

    [Fact]
    public void PackageIdentityIsDetectedOnlyOnce()
    {
        var calls = 0;
        var service = new ApplicationDistributionService(() =>
        {
            calls++;
            return true;
        });

        _ = service.Channel;
        _ = service.Channel;

        Assert.Equal(1, calls);
    }
}
