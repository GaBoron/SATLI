using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class LoadingTipServiceTests
{
    [Fact]
    public async Task FirstLoadCreatesEditableDefaultFile()
    {
        var directory = TemporaryDirectory();
        var service = new LoadingTipService(directory, _ => 0);

        var tip = await service.GetTipAsync();

        Assert.Equal(LoadingTipService.DefaultTips[0], tip);
        Assert.True(File.Exists(service.FilePath));
        var content = await File.ReadAllTextAsync(service.FilePath);
        Assert.Contains("每行一条", content);
        Assert.Contains(LoadingTipService.DefaultTips[0], content);
    }

    [Fact]
    public void DefaultFileContainsTheCompleteTipSet()
    {
        Assert.Equal(39, LoadingTipService.DefaultTips.Count);
        Assert.Equal("正在给每个成就贴上中文翻译。", LoadingTipService.DefaultTips[0]);
        Assert.Equal("sk-kfccrazythursdayvme50", LoadingTipService.DefaultTips[^1]);
        Assert.Contains(
            "有问题可以给 SATLI.support@proton.me 发邮件。",
            LoadingTipService.DefaultTips);
    }

    [Fact]
    public async Task CustomFileIgnoresCommentsAndSelectsUserTip()
    {
        var directory = TemporaryDirectory();
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "loading-tips.txt"),
            "# comment\n\n第一条自定义 Tip\n  第二条自定义 Tip  \n");
        var service = new LoadingTipService(directory, _ => 1);

        var tip = await service.GetTipAsync();

        Assert.Equal("第二条自定义 Tip", tip);
    }

    private static string TemporaryDirectory() => Path.Combine(
        Path.GetTempPath(),
        "satl-loading-tip-tests",
        Guid.NewGuid().ToString("N"));
}
