using Satli_Gui.Services;
using Satli_Gui.ViewModels;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class GameLoadingProgressTests
{
    [Fact]
    public void TracksPlanLookupAndItems()
    {
        var progress = new GameLoadingProgress();
        progress.Start("正在扫描…");

        progress.Handle(SatliCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"plan\",\"payload\":{\"count\":4}}"
        ));
        Assert.Equal(0, progress.Value);
        Assert.Equal(4, progress.Maximum);
        Assert.Contains("0/4", progress.Text);

        progress.Handle(SatliCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"progress\",\"payload\":{\"current\":1,\"total\":2,\"message\":\"正在联网查询游戏名称 1/2\"}}"
        ));
        Assert.Equal(1, progress.Value);
        Assert.Equal(2, progress.Maximum);
        Assert.Contains("1/2", progress.Text);

        progress.Handle(SatliCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"progress\",\"payload\":{\"current\":0,\"total\":4,\"message\":\"正在加载游戏 0/4\"}}"
        ));
        progress.Handle(SatliCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"item-succeeded\",\"payload\":{\"position\":1}}"
        ));
        Assert.Equal(1, progress.Value);
        Assert.Equal(4, progress.Maximum);
        Assert.Contains("1/4", progress.Text);
    }
}
