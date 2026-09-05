using Satli_Gui.Models;

namespace Satli_Gui.Services;

public static class SteamLibrarySettingsValidator
{
    public static SteamLibrarySettings Normalize(SteamLibrarySettings? settings)
    {
        settings ??= new SteamLibrarySettings();
        return new SteamLibrarySettings
        {
            Enabled = settings.Enabled,
            SteamId = settings.SteamId.Trim(),
            ApiKey = settings.ApiKey.Trim(),
            ApiKeyChanged = settings.ApiKeyChanged,
            ProtectedApiKey = settings.ProtectedApiKey,
        };
    }

    public static SteamLibrarySettings RequireConfigured(SteamLibrarySettings? settings)
    {
        var normalized = Normalize(settings);
        if (!normalized.SteamId.All(char.IsAsciiDigit) || normalized.SteamId.Length != 17)
        {
            throw new ArgumentException("SteamID64 应为 17 位数字。");
        }
        if (normalized.ApiKey.Length != 32 || !normalized.ApiKey.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Steam Web API Key 应为 32 位十六进制字符。");
        }
        return normalized;
    }

    public static bool IsConfigured(SteamLibrarySettings? settings)
    {
        try
        {
            _ = RequireConfigured(settings);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
