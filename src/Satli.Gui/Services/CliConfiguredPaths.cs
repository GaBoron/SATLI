using Satli_Gui.Models;

namespace Satli_Gui.Services;

public static class CliConfiguredPaths
{
    public static void AppendDataDirectory(List<string> arguments, GuiSettings settings)
    {
        var dataDirectory = string.IsNullOrWhiteSpace(settings.DataDirectory)
            ? SettingsService.DefaultDataDirectory
            : settings.DataDirectory;
        arguments.AddRange(["--data-dir", dataDirectory]);
    }

    public static void AppendSteamDirectory(
        List<string> arguments,
        GuiSettings settings,
        string? detectedSteamDirectory = null)
    {
        var steamDirectory = string.IsNullOrWhiteSpace(settings.SteamDirectory)
            ? detectedSteamDirectory
            : settings.SteamDirectory;
        if (!string.IsNullOrWhiteSpace(steamDirectory))
        {
            arguments.AddRange(["--steam-dir", steamDirectory]);
        }
    }
}
