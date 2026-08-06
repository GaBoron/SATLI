using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class StoreUpdateServiceTests
{
    [Fact]
    public async Task NoStorePackagesMeansCurrentVersionIsLatest()
    {
        var releaseChecks = 0;
        var service = new StoreUpdateService(
            new FakeStorePackageUpdateSource(),
            _ =>
            {
                releaseChecks++;
                return Task.FromResult(GitHubRelease("0.13.0", "新版内容"));
            },
            new Version(0, 12, 0));

        var result = await service.CheckAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.True(result.IsMicrosoftStoreUpdate);
        Assert.Equal("0.12.0", result.CurrentVersion);
        Assert.Equal("0.12.0", result.LatestVersion);
        Assert.Equal(StoreUpdateService.ProductPageUri, result.ReleasePage);
        Assert.Equal(0, releaseChecks);
    }

    [Fact]
    public async Task StoreUpdateUsesMatchingGitHubReleaseNotes()
    {
        var service = new StoreUpdateService(
            new FakeStorePackageUpdateSource(new Version(0, 13, 0, 0)),
            _ => Task.FromResult(GitHubRelease("0.13.0", "- 新增 Store 更新检查")),
            new Version(0, 12, 0));

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.True(result.IsMicrosoftStoreUpdate);
        Assert.Equal("0.13.0", result.LatestVersion);
        Assert.Equal("- 新增 Store 更新检查", result.ReleaseNotes);
        Assert.Null(result.InstallerDownload);
    }

    [Fact]
    public async Task StoreUpdateDoesNotShowNotesFromADifferentVersion()
    {
        var service = new StoreUpdateService(
            new FakeStorePackageUpdateSource(new Version(0, 13, 0, 1)),
            _ => Task.FromResult(GitHubRelease("0.14.0", "不应显示的内容")),
            new Version(0, 12, 0));

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.13.0", result.LatestVersion);
        Assert.DoesNotContain("不应显示的内容", result.ReleaseNotes);
        Assert.Contains("详细更新内容暂时无法读取", result.ReleaseNotes);
    }

    private static UpdateCheckResult GitHubRelease(string version, string notes) => new(
        true,
        "0.12.0",
        version,
        new Uri($"https://example.test/releases/{version}"),
        null,
        null,
        notes,
        $"发现新版本 v{version}。");

    private sealed class FakeStorePackageUpdateSource(params Version[] versions)
        : IStorePackageUpdateSource
    {
        public Task<IReadOnlyList<StorePackageUpdateInfo>> GetAvailableUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<StorePackageUpdateInfo> updates = versions
                .Select(version => new StorePackageUpdateInfo(version))
                .ToArray();
            return Task.FromResult(updates);
        }
    }
}
