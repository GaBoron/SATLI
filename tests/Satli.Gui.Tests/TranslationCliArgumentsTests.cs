using Satli_Gui.Models;
using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class TranslationCliArgumentsTests
{
    private const string ReservedTestSteamId = "76561197960265728";

    [Fact]
    public void SchemaInspectUsesConfiguredSteamAndDataDirectories()
    {
        var settings = new GuiSettings
        {
            SteamDirectory = @"C:\Steam",
            DataDirectory = @"C:\SATLIData",
        };
        var builder = new TranslationCliArguments(() => settings);

        var arguments = builder.SchemaInspect("123");

        Assert.Equal(["schema", "inspect", "123", "--jsonl"], arguments.Take(4));
        Assert.Contains("--steam-dir", arguments);
        Assert.Contains(@"C:\Steam", arguments);
        Assert.Contains("--data-dir", arguments);
        Assert.Contains(@"C:\SATLIData", arguments);
    }

    [Fact]
    public void RestorePreviewBuildsReadOnlyContentRequest()
    {
        var builder = new TranslationCliArguments(() => new GuiSettings());
        var game = new GameItem { AppId = "123", GameName = "Local Game" };

        var arguments = builder.Restore(
            [game],
            dryRun: true,
            yes: false,
            force: true,
            previewContent: true);

        Assert.Equal("restore", arguments[0]);
        Assert.Contains("123", arguments);
        Assert.Contains("--force", arguments);
        Assert.Contains("--dry-run", arguments);
        Assert.Contains("--preview-content", arguments);
        Assert.Contains("--jsonl", arguments);
        Assert.DoesNotContain("--yes", arguments);
    }

    [Fact]
    public void DefaultPathsPreserveOriginalUserContextForElevatedOperations()
    {
        var builder = new TranslationCliArguments(
            () => new GuiSettings(),
            () => @"D:\DetectedSteam");
        var game = new GameItem { AppId = "123", GameName = "Local Game" };

        var arguments = builder.Install(
            [game],
            dryRun: false,
            yes: true,
            previewContent: false);

        Assert.Contains(@"D:\DetectedSteam", arguments);
        Assert.Contains(SettingsService.DefaultDataDirectory, arguments);
    }

    [Fact]
    public void ScanCanUseCachedCatalogWhileIncludingSteamInventory()
    {
        const string apiKey = "0123456789abcdef0123456789abcdef";
        var settings = new GuiSettings
        {
            SteamLibrary = new SteamLibrarySettings
            {
                Enabled = true,
                SteamId = ReservedTestSteamId,
                ApiKey = apiKey,
            },
        };

        var arguments = new TranslationCliArguments(() => settings)
            .Scan(useCatalogCache: true, out var warning);

        Assert.Null(warning);
        Assert.Contains("--catalog-cache-only", arguments);
        Assert.DoesNotContain("--offline", arguments);
        Assert.Contains("--include-owned-games", arguments);
        Assert.Contains(ReservedTestSteamId, arguments);
        Assert.DoesNotContain(apiKey, arguments);
    }

    [Fact]
    public void ProtectLockCarriesRiskConfirmationAndDetectedSteamDirectory()
    {
        var builder = new TranslationCliArguments(
            () => new GuiSettings(),
            () => @"D:\DetectedSteam");
        var game = new GameItem { AppId = "123", GameName = "Game" };

        var arguments = builder.Protect([game], enable: true);

        Assert.Equal(["protect", "lock", "123"], arguments.Take(3));
        Assert.Contains("--yes", arguments);
        Assert.Contains("--jsonl", arguments);
        Assert.Contains(@"D:\DetectedSteam", arguments);
    }
}
