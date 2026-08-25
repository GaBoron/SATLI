namespace Satli_Gui.Services;

public enum SteamRunningChoice
{
    Cancel,
    ForceClose,
    CloseAndRestart,
}

public sealed record SteamRestartTarget(string? ExecutablePath);

public sealed record SteamMutationOutcome(
    bool OperationStarted,
    bool OperationSucceeded,
    bool SteamRestarted,
    string RestartWarning = "");

public interface ISteamProcessController
{
    bool IsRunning();
    Task ForceCloseAsync();
    Task<SteamRestartTarget> CloseForRestartAsync();
    Task RestartAsync(SteamRestartTarget target);
}

public sealed class SteamMutationWorkflow
{
    private readonly ISteamProcessController _steam;

    public SteamMutationWorkflow(ISteamProcessController steam)
    {
        _steam = steam;
    }

    public async Task<SteamMutationOutcome> ExecuteAsync(
        Func<Task<SteamRunningChoice>> chooseAsync,
        Func<Task<bool>> operationAsync)
    {
        if (!_steam.IsRunning())
        {
            return new SteamMutationOutcome(
                OperationStarted: true,
                OperationSucceeded: await operationAsync(),
                SteamRestarted: false);
        }

        var choice = await chooseAsync();
        if (choice == SteamRunningChoice.Cancel)
        {
            return new SteamMutationOutcome(false, false, false);
        }

        SteamRestartTarget? restartTarget = null;
        if (choice == SteamRunningChoice.ForceClose)
        {
            await _steam.ForceCloseAsync();
        }
        else
        {
            restartTarget = await _steam.CloseForRestartAsync();
        }

        var succeeded = await operationAsync();
        if (!succeeded || restartTarget is null)
        {
            return new SteamMutationOutcome(true, succeeded, false);
        }

        try
        {
            await _steam.RestartAsync(restartTarget);
            return new SteamMutationOutcome(true, true, true);
        }
        catch (Exception exception)
        {
            return new SteamMutationOutcome(
                true,
                true,
                false,
                $"翻译操作已成功，但无法重新启动 Steam：{exception.Message}");
        }
    }
}
