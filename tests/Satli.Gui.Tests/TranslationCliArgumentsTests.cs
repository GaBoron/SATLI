using Satli_Gui.Models;
using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class TranslationCliArgumentsTests
{
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
}
