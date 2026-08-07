using Satli_Gui.Models;

namespace Satli_Gui.Services;

public static class SteamLibraryCliOptions
{
    public static string? AppendScanArguments(
        List<string> arguments,
        GuiSettings settings)
    {
        var steamLibrary = SteamLibrarySettingsValidator.Normalize(settings.SteamLibrary);
        if (!steamLibrary.Enabled || settings.Offline)
        {
            return null;
        }
        if (!SteamLibrarySettingsValidator.IsConfigured(steamLibrary))
        {
            return "Steam 游戏库补全已开启，但 API Key 或 SteamID64 尚未填写完整；本次仅使用本地扫描。";
        }
        arguments.Add("--include-owned-games");
        arguments.Add("--owned-account");
        arguments.Add(steamLibrary.SteamId);
        return null;
    }
}
