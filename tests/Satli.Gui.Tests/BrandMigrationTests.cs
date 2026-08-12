using System.Text;
using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class BrandMigrationTests
{
    [Fact]
    public void ApplicationDataDirectoryMovesLegacyContentOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-data-migration-{Guid.NewGuid():N}");
        var legacy = Path.Combine(root, "SteamAchievementTranslationInstaller");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "gui-settings.json"), "{}");
        var legacyUpdates = Path.Combine(legacy, "updates");
        Directory.CreateDirectory(legacyUpdates);
        File.WriteAllText(
            Path.Combine(legacyUpdates, "SATLInstaller-Setup-v0.12.0.exe"),
            "stale");
        try
        {
            var current = ApplicationDataPaths.MigrateDefaultDirectory(root);

            Assert.Equal(Path.Combine(root, "SATLI"), current);
            Assert.True(File.Exists(Path.Combine(current, "gui-settings.json")));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(current, "updates")));
            Assert.False(Directory.Exists(legacy));
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
    public void ApplicationDataDirectoryCopiesWhenMoveIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-data-copy-{Guid.NewGuid():N}");
        var legacy = Path.Combine(root, "SteamAchievementTranslationInstaller");
        var nested = Path.Combine(legacy, "cache");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "index.json"), "{}");
        try
        {
            var current = ApplicationDataPaths.MigrateDefaultDirectory(
                root,
                (_, _) => throw new IOException("Simulated MSIX move failure."));

            Assert.True(File.Exists(Path.Combine(current, "cache", "index.json")));
            Assert.True(Directory.Exists(legacy));
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
    public void ApplicationDataDirectoryMergesWithoutOverwritingCurrentContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-data-merge-{Guid.NewGuid():N}");
        var legacy = Path.Combine(root, "SteamAchievementTranslationInstaller");
        var current = Path.Combine(root, "SATLI");
        Directory.CreateDirectory(Path.Combine(legacy, "cache"));
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "gui-settings.json"), "legacy");
        File.WriteAllText(Path.Combine(legacy, "cache", "index.json"), "{}");
        File.WriteAllText(Path.Combine(current, "gui-settings.json"), "current");
        try
        {
            var migrated = ApplicationDataPaths.MigrateDefaultDirectory(root);

            Assert.Equal(current, migrated);
            Assert.Equal("current", File.ReadAllText(Path.Combine(current, "gui-settings.json")));
            Assert.True(File.Exists(Path.Combine(current, "cache", "index.json")));
            Assert.True(Directory.Exists(legacy));
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
    public void StoredLegacyDefaultDirectoryMovesToCurrentDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-stored-path-{Guid.NewGuid():N}");
        var legacy = Path.Combine(root, "SteamAchievementTranslationInstaller");

        var migrated = ApplicationDataPaths.MigrateStoredDataDirectory(legacy, root);

        Assert.Equal(Path.Combine(root, "SATLI"), migrated);
    }

    [Fact]
    public void WebViewUserDataUsesApplicationDataDirectory()
    {
        const string applicationData = @"C:\Users\Test\AppData\Local\SATLI";

        var webViewData = ApplicationDataPaths.WebViewUserDataDirectoryFor(applicationData);

        Assert.Equal(Path.Combine(applicationData, "WebView2"), webViewData);
    }

    [Fact]
    public void ProtectedDataIsRewrittenAfterReadingLegacyEntropy()
    {
        var currentEntropy = Encoding.UTF8.GetBytes("SATLI.TestSecret.v1");
        var legacyEntropy = Encoding.UTF8.GetBytes("SATLInstaller.TestSecret.v1");
        var legacyValue = ProtectedDataMigration.Protect("secret", legacyEntropy);

        var migrated = ProtectedDataMigration.Unprotect(
            legacyValue,
            currentEntropy,
            legacyEntropy);
        var currentValue = ProtectedDataMigration.Protect(migrated.Value, currentEntropy);
        var verified = ProtectedDataMigration.Unprotect(currentValue, currentEntropy);

        Assert.Equal("secret", migrated.Value);
        Assert.True(migrated.RequiresRewrite);
        Assert.Equal("secret", verified.Value);
        Assert.False(verified.RequiresRewrite);
    }
}
