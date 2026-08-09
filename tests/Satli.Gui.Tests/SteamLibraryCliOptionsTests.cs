using Satli_Gui.Models;
using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class SteamLibraryCliOptionsTests
{
    private const string ReservedTestSteamId = "76561197960265728";

    [Fact]
    public void NeverPlacesApiKeyInArguments()
    {
        const string apiKey = "0123456789abcdef0123456789abcdef";
        var arguments = new List<string> { "scan", "--jsonl" };
        var warning = SteamLibraryCliOptions.AppendScanArguments(
            arguments,
            new GuiSettings
            {
                SteamLibrary = new SteamLibrarySettings
                {
                    Enabled = true,
                    SteamId = ReservedTestSteamId,
                    ApiKey = apiKey,
                },
            });

        Assert.Null(warning);
        Assert.Contains("--include-owned-games", arguments);
        Assert.Contains("--owned-account", arguments);
        Assert.Contains(ReservedTestSteamId, arguments);
        Assert.DoesNotContain(apiKey, arguments);
    }

    [Fact]
    public void SkipsIncompleteConfiguration()
    {
        var arguments = new List<string> { "scan" };
        var warning = SteamLibraryCliOptions.AppendScanArguments(
            arguments,
            new GuiSettings
            {
                SteamLibrary = new SteamLibrarySettings { Enabled = true },
            });

        Assert.Contains("尚未填写完整", warning);
        Assert.DoesNotContain("--include-owned-games", arguments);
    }

}
