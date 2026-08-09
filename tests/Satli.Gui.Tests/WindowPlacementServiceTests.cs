using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class WindowPlacementServiceTests
{
    [Fact]
    public void DefaultsTo1280By720AndCenters()
    {
        var placement = WindowPlacementService.CenterDefault(100, 50, 1920, 1080);

        Assert.Equal(new WindowPlacement(420, 230, 1280, 720), placement);
    }

    [Fact]
    public void FitsRestoredBoundsToCurrentWorkArea()
    {
        var placement = WindowPlacementService.FitToWorkArea(
            new WindowPlacement(3000, -800, 2400, 200),
            workX: 0,
            workY: 0,
            workWidth: 1920,
            workHeight: 1040);

        Assert.Equal(new WindowPlacement(0, 0, 1920, 480), placement);
    }

    [Fact]
    public void RoundTripsThroughItsOwnSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-window-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "window-placement.json");
        try
        {
            var service = new WindowPlacementService(path);
            var expected = new WindowPlacement(140, 90, 1440, 900);

            service.Save(expected);

            Assert.Equal(expected, service.Load());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
