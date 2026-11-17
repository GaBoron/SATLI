namespace Satli_Gui.Services;

/// <summary>Runs component setup after the user's single confirmation, then starts the selected Steam.</summary>
public sealed class LiteComponentSetupWorkflow(ISteamProcessController steam)
{
    public async Task<SteamMutationOutcome> ExecuteAsync(string steamDirectory, Func<Task> installAsync,
        Action<string>? progress = null)
    {
        var restartTarget = new SteamRestartTarget(Path.Combine(Path.GetFullPath(steamDirectory), "steam.exe"), ShowWindow: true);
        if (steam.IsRunning())
        {
            progress?.Invoke("正在关闭 Steam…");
            try { await steam.CloseForRestartAsync(); }
            catch (SteamProcessOperationException exception) when (exception.TimedOut)
            {
                progress?.Invoke("Steam 退出超时，正在强制关闭…");
                await steam.ForceCloseAsync();
            }
        }
        // A bootstrap process may reappear while Steam finishes shutting down.
        if (steam.IsRunning())
        {
            progress?.Invoke("正在结束剩余 Steam 进程…");
            await steam.ForceCloseAsync();
        }
        if (steam.IsRunning()) throw new SteamProcessOperationException("无法结束 Steam 进程，未开始组件安装。");

        progress?.Invoke("正在安装 Millennium 和 SATLI-lite…");
        await installAsync();
        progress?.Invoke("组件安装完成，正在启动 Steam…");
        try
        {
            await steam.RestartAsync(restartTarget);
            return new(true, true, true);
        }
        catch (Exception exception)
        {
            return new(true, true, false, $"组件已安装，但无法启动 Steam：{exception.Message}");
        }
    }
}
