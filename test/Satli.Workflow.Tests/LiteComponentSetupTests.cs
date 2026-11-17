using Satli_Gui.Services;
using Xunit;

namespace Satli.Workflow.Tests;

public sealed class LiteComponentSetupTests
{
    [Fact]
    public async Task ConfirmationRunsCloseInstallRestartInOrder()
    {
        var steam = new SteamStub();
        var outcome = await new LiteComponentSetupWorkflow(steam).ExecuteAsync("C:/SelectedSteam", () =>
        {
            Assert.False(steam.Running);
            steam.Events.Add("install");
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { "close", "install", "restart" }, steam.Events);
        Assert.True(outcome.OperationSucceeded && outcome.SteamRestarted);
        Assert.Equal(Path.GetFullPath("C:/SelectedSteam/steam.exe"), steam.RestartTarget!.ExecutablePath);
        Assert.True(steam.RestartTarget.ShowWindow);
    }

    [Fact]
    public async Task ExitTimeoutFallsBackToForceWithoutAnotherPrompt()
    {
        var steam = new SteamStub { CloseError = new SteamProcessOperationException("timeout", timedOut: true) };
        await new LiteComponentSetupWorkflow(steam).ExecuteAsync("C:/SelectedSteam", () =>
        {
            steam.Events.Add("install");
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { "close", "force", "install", "restart" }, steam.Events);
    }

    [Fact]
    public async Task RemainingBootstrapIsStoppedBeforeInstall()
    {
        var steam = new SteamStub { RemainsRunningAfterClose = true };
        await new LiteComponentSetupWorkflow(steam).ExecuteAsync("C:/SelectedSteam", () =>
        {
            Assert.False(steam.Running);
            steam.Events.Add("install");
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { "close", "force", "install", "restart" }, steam.Events);
    }

    [Fact]
    public async Task AlreadyStoppedSteamIsStartedAfterInstallation()
    {
        var steam = new SteamStub { Running = false };
        await new LiteComponentSetupWorkflow(steam).ExecuteAsync("C:/SelectedSteam", () =>
        {
            steam.Events.Add("install");
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { "install", "restart" }, steam.Events);
    }

    [Fact]
    public async Task ShutdownFailureNeverStartsInstallation()
    {
        var steam = new SteamStub { CloseError = new SteamProcessOperationException("permission denied") };
        await Assert.ThrowsAsync<SteamProcessOperationException>(() =>
            new LiteComponentSetupWorkflow(steam).ExecuteAsync("C:/SelectedSteam", () => throw new Exception("must not run")));
        Assert.Equal(new[] { "close" }, steam.Events);
    }

    [Fact]
    public async Task InstallationFailureDoesNotReportSuccessOrRestart()
    {
        var steam = new SteamStub();
        await Assert.ThrowsAsync<IOException>(() => new LiteComponentSetupWorkflow(steam).ExecuteAsync(
            "C:/SelectedSteam", () => throw new IOException("download interrupted")));
        Assert.Equal(new[] { "close" }, steam.Events);
    }

    [Fact]
    public async Task RestartFailurePreservesSuccessfulInstallWithWarning()
    {
        var steam = new SteamStub { RestartError = true };
        var outcome = await new LiteComponentSetupWorkflow(steam).ExecuteAsync("C:/SelectedSteam", () => Task.CompletedTask);
        Assert.True(outcome.OperationSucceeded);
        Assert.False(outcome.SteamRestarted);
        Assert.Contains("组件已安装", outcome.RestartWarning);
    }

    private sealed class SteamStub : ISteamProcessController
    {
        public List<string> Events { get; } = [];
        public bool Running { get; set; } = true;
        public bool RemainsRunningAfterClose { get; init; }
        public SteamProcessOperationException? CloseError { get; init; }
        public bool RestartError { get; init; }
        public SteamRestartTarget? RestartTarget { get; private set; }
        public bool IsRunning() => Running;
        public Task ForceCloseAsync() { Events.Add("force"); Running = false; return Task.CompletedTask; }
        public Task<SteamRestartTarget> CloseForRestartAsync()
        {
            Events.Add("close");
            if (CloseError is not null) throw CloseError;
            Running = RemainsRunningAfterClose;
            return Task.FromResult(new SteamRestartTarget("C:/OriginalSteam/steam.exe"));
        }
        public Task RestartAsync(SteamRestartTarget target)
        {
            Events.Add("restart");
            RestartTarget = target;
            if (RestartError) throw new IOException("launch failed");
            Running = true;
            return Task.CompletedTask;
        }
    }
}
