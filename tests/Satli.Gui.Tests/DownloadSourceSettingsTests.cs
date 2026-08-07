using Satli_Gui.Models;
using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class DownloadSourceSettingsTests
{
    [Fact]
    public void DefaultsPrioritizeGitHubRawAndSeparateFallbackOrders()
    {
        var settings = DownloadSourceCatalog.Normalize(new DownloadSourceSettings());

        Assert.Equal(
            ["github", "jsdelivr", "jsdelivr-fastly", "staticdelivr"],
            settings.IndexSourceOrder);
        Assert.Equal(
            ["jsdelivr", "jsdelivr-fastly", "github"],
            settings.FileSourceOrder);
        Assert.StartsWith(
            "https://raw.githubusercontent.com/",
            DownloadSourceCatalog.CatalogEndpoints(settings)[0].AbsoluteUri);
        Assert.Equal(
            "jsdelivr;jsdelivr-fastly;github",
            DownloadSourceCatalog.EnvironmentOrder(settings.FileSourceOrder));
    }

    [Fact]
    public void NormalizePreservesOrderAndRestoresMissingKnownSources()
    {
        var settings = DownloadSourceCatalog.Normalize(new DownloadSourceSettings
        {
            IndexSourceOrder = ["staticdelivr", "github", "unknown", "github"],
            FileSourceOrder = ["github", "jsdelivr"],
        });

        Assert.Equal(
            ["staticdelivr", "github", "jsdelivr", "jsdelivr-fastly"],
            settings.IndexSourceOrder);
        Assert.Equal(
            ["github", "jsdelivr", "jsdelivr-fastly"],
            settings.FileSourceOrder);
    }

    [Fact]
    public async Task SettingsServicePersistsIndependentOrders()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"satli-sources-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings
            {
                DownloadSources = new DownloadSourceSettings
                {
                    IndexSourceOrder =
                        ["github", "staticdelivr", "jsdelivr-fastly", "jsdelivr"],
                    FileSourceOrder = ["github", "jsdelivr", "jsdelivr-fastly"],
                },
            });

            var loaded = await service.LoadAsync();

            Assert.Equal(
                ["github", "staticdelivr", "jsdelivr-fastly", "jsdelivr"],
                loaded.DownloadSources.IndexSourceOrder);
            Assert.Equal(
                ["github", "jsdelivr", "jsdelivr-fastly"],
                loaded.DownloadSources.FileSourceOrder);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
