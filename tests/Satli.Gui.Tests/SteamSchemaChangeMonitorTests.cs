using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class SteamSchemaChangeMonitorTests
{
    [Theory]
    [InlineData(@"C:\Steam\appcache\stats\UserGameStatsSchema_730.bin", "730")]
    [InlineData("UserGameStatsSchema_12345678901234567890.BIN", "12345678901234567890")]
    public void ParsesValidSchemaFileNames(string path, string expectedAppId)
    {
        Assert.True(SteamSchemaChangeMonitor.TryGetAppId(path, out var appId));
        Assert.Equal(expectedAppId, appId);
    }

    [Theory]
    [InlineData("UserGameStatsSchema_0.bin")]
    [InlineData("UserGameStatsSchema_bad.bin")]
    [InlineData(".UserGameStatsSchema_730.bin.tmp")]
    public void RejectsNonSchemaFileNames(string path)
    {
        Assert.False(SteamSchemaChangeMonitor.TryGetAppId(path, out _));
    }

    [Fact]
    public async Task EmitsDebouncedChangeForSteamSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-monitor-{Guid.NewGuid():N}");
        var stats = Path.Combine(root, "appcache", "stats");
        Directory.CreateDirectory(stats);
        try
        {
            using var monitor = new SteamSchemaChangeMonitor();
            var observed = new TaskCompletionSource<SteamSchemaChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.SchemaChanged += (_, change) => observed.TrySetResult(change);
            monitor.Configure(root);

            await File.WriteAllBytesAsync(
                Path.Combine(stats, "UserGameStatsSchema_730.bin"),
                [1, 2, 3]);

            var change = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("730", change.AppId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SuppressesChangesForEntireOwnedOperationAndCooldown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-monitor-{Guid.NewGuid():N}");
        var stats = Path.Combine(root, "appcache", "stats");
        var schema = Path.Combine(stats, "UserGameStatsSchema_730.bin");
        Directory.CreateDirectory(stats);
        try
        {
            using var monitor = new SteamSchemaChangeMonitor();
            var observed = new TaskCompletionSource<SteamSchemaChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.SchemaChanged += (_, change) => observed.TrySetResult(change);
            monitor.Configure(root);

            using (monitor.BeginSuppression(["730"], TimeSpan.FromMilliseconds(250)))
            {
                await File.WriteAllBytesAsync(schema, [1]);
                await Task.Delay(1000);
                Assert.False(observed.Task.IsCompleted);
            }

            await File.WriteAllBytesAsync(schema, [2]);
            await Task.Delay(500);
            Assert.False(observed.Task.IsCompleted);

            await File.WriteAllBytesAsync(schema, [3]);
            var change = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("730", change.AppId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
