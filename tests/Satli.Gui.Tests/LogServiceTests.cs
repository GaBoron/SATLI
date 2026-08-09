using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class LogServiceTests
{
    [Fact]
    public async Task WritesFiltersAndClearsLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-log-test-{Guid.NewGuid():N}");
        try
        {
            var service = new LogService(root);
            service.Configure(enabled: true, level: "detailed", retentionDays: 30);
            await service.WriteAsync("信息", "测试", "标准记录");
            await service.WriteAsync("详细", "测试", "详细记录", detailed: true);
            await service.WriteAsync("调试", "测试", "调试记录", debug: true);

            var content = await service.ReadRecentAsync();

            Assert.Contains("标准记录", content);
            Assert.Contains("详细记录", content);
            Assert.DoesNotContain("调试记录", content);
            service.Configure(enabled: true, level: "debug", retentionDays: 30);
            Assert.True(service.IsDebugEnabled);
            await service.WriteAsync("调试", "测试", "调试记录", debug: true);
            Assert.Contains("调试记录", await service.ReadRecentAsync());
            service.Configure(enabled: false, level: "detailed", retentionDays: 30);
            Assert.False(service.IsDebugEnabled);
            await service.WriteAsync("信息", "测试", "不应写入");
            Assert.DoesNotContain("不应写入", await service.ReadRecentAsync());

            await service.ClearAsync();
            Assert.Equal(string.Empty, await service.ReadRecentAsync());
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
    public async Task AppliesAllThreeVerbosityThresholds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-log-test-{Guid.NewGuid():N}");
        try
        {
            var service = new LogService(root);
            service.Configure(enabled: true, level: "standard", retentionDays: 30);
            await service.WriteAsync("信息", "测试", "普通可见");
            await service.WriteAsync("详细", "测试", "详尽隐藏", detailed: true);
            await service.WriteAsync("调试", "测试", "调试隐藏", debug: true);
            var standard = await service.ReadRecentAsync();
            Assert.Contains("普通可见", standard);
            Assert.DoesNotContain("详尽隐藏", standard);
            Assert.DoesNotContain("调试隐藏", standard);

            service.Configure(enabled: true, level: "detailed", retentionDays: 30);
            await service.WriteAsync("详细", "测试", "详尽可见", detailed: true);
            await service.WriteAsync("调试", "测试", "调试仍隐藏", debug: true);
            var detailed = await service.ReadRecentAsync();
            Assert.Contains("详尽可见", detailed);
            Assert.DoesNotContain("调试仍隐藏", detailed);
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
    public async Task AddsExceptionDetailByVerbosity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-log-test-{Guid.NewGuid():N}");
        try
        {
            var service = new LogService(root);
            var exception = CaptureTestException();

            service.Configure(enabled: true, level: "standard", retentionDays: 30);
            await service.WriteAsync("错误", "测试", "操作失败");
            await service.WriteExceptionDetailsAsync("测试", exception);
            var standard = await service.ReadRecentAsync();
            Assert.Contains("操作失败", standard);
            Assert.DoesNotContain("异常类型=", standard);
            Assert.DoesNotContain(nameof(CaptureTestException), standard);

            await service.ClearAsync();
            service.Configure(enabled: true, level: "detailed", retentionDays: 30);
            await service.WriteExceptionDetailsAsync("测试", exception);
            var detailed = await service.ReadRecentAsync();
            Assert.Contains("异常类型=System.InvalidOperationException", detailed);
            Assert.Contains("HRESULT=", detailed);
            Assert.DoesNotContain(nameof(CaptureTestException), detailed);

            await service.ClearAsync();
            service.Configure(enabled: true, level: "debug", retentionDays: 30);
            await service.WriteExceptionDetailsAsync("测试", exception);
            var debug = await service.ReadRecentAsync();
            Assert.Contains("异常类型=System.InvalidOperationException", debug);
            Assert.Contains(nameof(CaptureTestException), debug);
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
    public async Task ReadsOnlyTheLatestLogFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-log-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllLinesAsync(
                Path.Combine(root, "satli-gui-2026-07-15.log"),
                ["旧文件第一行", "旧文件第二行"]);
            await File.WriteAllLinesAsync(
                Path.Combine(root, "satli-gui-2026-07-16.log"),
                ["最新文件第一行", "最新文件第二行", "最新文件第三行"]);

            var content = await new LogService(root).ReadRecentAsync(maximumLines: 2);

            Assert.DoesNotContain("旧文件", content);
            Assert.DoesNotContain("最新文件第一行", content);
            Assert.Equal($"最新文件第二行{Environment.NewLine}最新文件第三行", content);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static Exception CaptureTestException()
    {
        try
        {
            throw new InvalidOperationException("用于测试的异常");
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
