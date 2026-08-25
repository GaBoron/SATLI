using System.Diagnostics;
using Microsoft.Win32;

namespace Satli_Gui.Services;

public sealed class SteamProcessOperationException : Exception
{
    public SteamProcessOperationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class SteamProcessController : ISteamProcessController
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(20);

    public bool IsRunning()
    {
        using var processes = new ProcessCollection(Process.GetProcessesByName("steam"));
        return processes.Any(process => !SafeHasExited(process));
    }

    public async Task ForceCloseAsync()
    {
        try
        {
            using var processes = new ProcessCollection(Process.GetProcessesByName("steam"));
            foreach (var process in processes.Where(process => !SafeHasExited(process)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (SafeHasExited(process))
                {
                }
            }
            await WaitForExitAsync("强制关闭");
        }
        catch (SteamProcessOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SteamProcessOperationException(
                $"无法强制关闭 Steam：{exception.Message}", exception);
        }
    }

    public async Task<SteamRestartTarget> CloseForRestartAsync()
    {
        var executablePath = FindExecutablePath();
        try
        {
            Process? shutdownRequest;
            if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
            {
                shutdownRequest = Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-shutdown",
                    UseShellExecute = true,
                });
            }
            else
            {
                shutdownRequest = Process.Start(new ProcessStartInfo
                {
                    FileName = "steam://exit",
                    UseShellExecute = true,
                });
            }
            shutdownRequest?.Dispose();
            await WaitForExitAsync("正常关闭");
            return new SteamRestartTarget(executablePath);
        }
        catch (SteamProcessOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SteamProcessOperationException(
                $"无法正常关闭 Steam：{exception.Message}", exception);
        }
    }

    public Task RestartAsync(SteamRestartTarget target)
    {
        Process? process;
        if (!string.IsNullOrWhiteSpace(target.ExecutablePath)
            && File.Exists(target.ExecutablePath))
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = target.ExecutablePath,
                Arguments = "-silent",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
            });
        }
        else
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "steam://open/main",
                UseShellExecute = true,
            });
        }
        if (process is null)
        {
            throw new InvalidOperationException("Windows 未能启动 Steam 进程。");
        }
        process.Dispose();
        return Task.CompletedTask;
    }

    private async Task WaitForExitAsync(string action)
    {
        var deadline = DateTimeOffset.UtcNow + ExitTimeout;
        while (IsRunning() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200);
        }
        if (IsRunning())
        {
            throw new SteamProcessOperationException(
                $"Steam 在等待 20 秒后仍未完成{action}，已取消翻译操作。可重试并选择“强制关闭 Steam”。");
        }
    }

    private static string? FindExecutablePath()
    {
        using (var processes = new ProcessCollection(Process.GetProcessesByName("steam")))
        {
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return Path.GetFullPath(path);
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                        or System.ComponentModel.Win32Exception
                        or NotSupportedException)
                {
                }
            }
        }

        using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var configured = steamKey?.GetValue("SteamExe") as string;
        return string.IsNullOrWhiteSpace(configured)
            ? null
            : Path.GetFullPath(configured.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private sealed class ProcessCollection : IDisposable, IEnumerable<Process>
    {
        private readonly Process[] _processes;

        public ProcessCollection(Process[] processes)
        {
            _processes = processes;
        }

        public IEnumerator<Process> GetEnumerator() =>
            ((IEnumerable<Process>)_processes).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            _processes.GetEnumerator();

        public void Dispose()
        {
            foreach (var process in _processes)
            {
                process.Dispose();
            }
        }
    }
}
