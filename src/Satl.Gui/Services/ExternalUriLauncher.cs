using System.Diagnostics;
using Windows.System;

namespace Satl_Gui.Services;

internal sealed class ExternalUriLauncher
{
    private readonly Func<Uri, Task<bool>> _launchWithWindowsAsync;
    private readonly Func<ProcessStartInfo, bool> _shellExecute;

    public ExternalUriLauncher(
        Func<Uri, Task<bool>>? launchWithWindowsAsync = null,
        Func<ProcessStartInfo, bool>? shellExecute = null)
    {
        _launchWithWindowsAsync = launchWithWindowsAsync ?? LaunchWithWindowsAsync;
        _shellExecute = shellExecute ?? StartWithShell;
    }

    public async Task<bool> LaunchAsync(Uri uri)
    {
        try
        {
            if (await _launchWithWindowsAsync(uri))
            {
                return true;
            }
        }
        catch
        {
            // Fall back to the registered Windows Shell handler below.
        }

        return TryShellExecute(uri);
    }

    private bool TryShellExecute(Uri uri)
    {
        try
        {
            return _shellExecute(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> LaunchWithWindowsAsync(Uri uri) =>
        await Launcher.LaunchUriAsync(uri);

    private static bool StartWithShell(ProcessStartInfo startInfo)
    {
        Process.Start(startInfo);
        return true;
    }
}
