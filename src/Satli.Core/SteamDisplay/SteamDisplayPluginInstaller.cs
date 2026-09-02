using System.Text.Json.Nodes;
using Satli.Core.FileSystem;

namespace Satli.Core.SteamDisplay;

public sealed record SteamDisplayPluginInstallResult(
    string PluginPath,
    bool Updated,
    bool RuntimeActive);

public sealed record SteamDisplayPluginStatus(
    string PluginPath,
    bool Installed,
    bool Current,
    bool RuntimeActive);

public static class SteamDisplayPluginInstaller
{
    public const string PluginFileName = "satli.star";

    public static bool IsMillenniumInstalled(string steamDirectory)
    {
        if (string.IsNullOrWhiteSpace(steamDirectory))
        {
            return false;
        }
        try
        {
            var steam = Path.GetFullPath(steamDirectory);
            return File.Exists(Path.Combine(
                    steam,
                    "millennium",
                    "lib",
                    "millennium.dll"))
                || File.Exists(Path.Combine(steam, "millennium.dll"));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    public static SteamDisplayPluginInstallResult EnsureInstalled(
        string steamDirectory,
        string bundledPluginPath)
    {
        var millenniumDirectory = Path.Combine(
            Path.GetFullPath(steamDirectory),
            "millennium");
        if (!IsMillenniumInstalled(steamDirectory))
        {
            throw new PreflightException(
                "未检测到完整的 Millennium 安装。请在 SATLI 中打开官方安装页完成安装，再重新锁定 Steam 成就显示。");
        }
        if (!File.Exists(bundledPluginPath))
        {
            throw new PreflightException(
                $"SATLI 安装中缺少 Millennium 插件：{bundledPluginPath}");
        }

        var status = Inspect(steamDirectory, bundledPluginPath);
        var pluginPath = status.PluginPath;
        var updated = !status.Current;
        if (updated)
        {
            FileOperations.CopyDurable(bundledPluginPath, pluginPath);
        }
        return new SteamDisplayPluginInstallResult(
            pluginPath,
            updated,
            IsRuntimeActive(steamDirectory));
    }

    public static SteamDisplayPluginStatus Inspect(
        string steamDirectory,
        string bundledPluginPath)
    {
        var pluginPath = Path.Combine(
            Path.GetFullPath(steamDirectory),
            "millennium",
            "plugins",
            PluginFileName);
        var installed = File.Exists(pluginPath);
        var current = false;
        if (installed && File.Exists(bundledPluginPath))
        {
            try
            {
                current = FileOperations.Sha256(pluginPath)
                    == FileOperations.Sha256(bundledPluginPath);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
                current = false;
            }
        }
        return new SteamDisplayPluginStatus(
            pluginPath,
            installed,
            current,
            IsRuntimeActive(steamDirectory));
    }

    public static bool IsRuntimeActive(string steamDirectory)
    {
        var path = RuntimeStatusPath(steamDirectory);
        if (!File.Exists(path))
        {
            return false;
        }
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            var timestamp = root?["heartbeat_unix"]?.GetValue<long>() ?? 0;
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(timestamp);
            return age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(15);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static string BundledPluginPath() => Path.Combine(
        AppContext.BaseDirectory,
        "Integrations",
        "Millennium",
        PluginFileName);

    public static string BridgePath(string steamDirectory) => Path.Combine(
        Path.GetFullPath(steamDirectory),
        "millennium",
        "config",
        "satli-bridge-v1.json");

    public static string RuntimeStatusPath(string steamDirectory) => Path.Combine(
        Path.GetFullPath(steamDirectory),
        "millennium",
        "config",
        "satli-runtime-v1.json");
}
