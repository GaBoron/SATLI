using System.Text.Json;
using Satli.Core.FileSystem;

namespace Satli.Core.SatliLite;

public static class MillenniumInstallation
{
    /// <summary>Reads Millennium's persisted plugin selection; missing or unreadable configuration is unknown.</summary>
    public static async Task<bool?> ReadLiteEnabledAsync(string steamDirectory, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetFullPath(steamDirectory), "millennium", "config", "config.json");
        try
        {
            FileOperations.EnsureNoReparsePoints(path);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
            const int limit = 1024 * 1024;
            if (input.Length > limit) return null;
            var bytes = new byte[(int)input.Length + 1];
            var count = await input.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            if (count > limit) return null;
            using var config = JsonDocument.Parse(bytes.AsMemory(0, count));
            if (config.RootElement.ValueKind != JsonValueKind.Object
                || !config.RootElement.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Object
                || !plugins.TryGetProperty("enabledPlugins", out var enabled) || enabled.ValueKind != JsonValueKind.Array)
                return null;
            var found = false;
            foreach (var plugin in enabled.EnumerateArray())
            {
                if (plugin.ValueKind != JsonValueKind.String) return null;
                found |= plugin.GetString() == "com.gaboron.satli-lite";
            }
            return found;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or SatliException)
        {
            return null;
        }
    }

    public static bool IsInstalled(string steamDirectory)
    {
        if (string.IsNullOrWhiteSpace(steamDirectory)) return false;
        try
        {
            var steam = Path.GetFullPath(steamDirectory);
            return File.Exists(Path.Combine(steam, "millennium", "lib", "millennium.dll"))
                || File.Exists(Path.Combine(steam, "millennium.dll"));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
