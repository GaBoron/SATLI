using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class ApplicationDistributionServiceTests
{
    [Fact]
    public void PackagedBuildUsesStoreManagedUpdates()
    {
        var service = new ApplicationDistributionService(() => true);

        Assert.True(service.UsesStoreManagedUpdates);
    }

    [Fact]
    public void UnpackagedBuildKeepsGitHubManagedUpdates()
    {
        var service = new ApplicationDistributionService(() => false);

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

        _ = service.UsesStoreManagedUpdates;
        _ = service.UsesStoreManagedUpdates;

        Assert.Equal(1, calls);
    }
}
