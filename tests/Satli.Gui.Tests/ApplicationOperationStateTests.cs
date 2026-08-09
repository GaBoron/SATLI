using Satli_Gui.ViewModels;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class ApplicationOperationStateTests
{
    [Fact]
    public void SerializesOperationsAndReturnsToReady()
    {
        var state = new ApplicationOperationState();

        Assert.True(state.TryBegin());
        Assert.False(state.TryBegin());
        state.SetStatus("正在测试…");
        Assert.Equal("正在测试…", state.StatusMessage);

        state.Complete();

        Assert.False(state.IsBusy);
        Assert.Equal("准备就绪", state.StatusMessage);
        Assert.True(state.TryBegin());
    }
}
