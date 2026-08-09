using Satli_Gui.Models;
using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task RoundTripUsesRequestedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-gui-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings
            {
                Offline = true,
                Theme = "dark",
                SteamDirectory = "C:\\Steam",
                LoggingEnabled = false,
                LogLevel = "detailed",
                LogRetentionDays = 90,
                LogWordWrap = false,
                CheckForUpdatesOnStartup = true,
            });

            var loaded = await service.LoadAsync();

            Assert.True(loaded.Offline);
            Assert.Equal("dark", loaded.Theme);
            Assert.Equal("C:\\Steam", loaded.SteamDirectory);
            Assert.False(loaded.LoggingEnabled);
            Assert.Equal("detailed", loaded.LogLevel);
            Assert.Equal(90, loaded.LogRetentionDays);
            Assert.False(loaded.LogWordWrap);
            Assert.True(loaded.CheckForUpdatesOnStartup);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NeverPersistsDebugModeAcrossRestarts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-gui-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings { LogLevel = "debug" });

            Assert.Equal("detailed", (await service.LoadAsync()).LogLevel);
            Assert.DoesNotContain(
                "debug",
                await File.ReadAllTextAsync(path),
                StringComparison.OrdinalIgnoreCase);
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
