using Satli.Core.FileSystem;
using Satli.Core.Steam;

namespace Satli.Core.SatliLite;

public sealed record LitePluginInstallResult(string PluginPath, bool AlreadyInstalled);

/// <summary>Installs the official plugin once and retires only the explicitly selected legacy package.</summary>
public static class LitePluginInstaller
{
    public static string PluginPath(string steamDirectory) => Path.Combine(
        Path.GetFullPath(steamDirectory), "millennium", "plugins", "satli-lite.star");

    public static string LegacyPluginPath(string steamDirectory) => Path.Combine(
        Path.GetFullPath(steamDirectory), "millennium", "plugins", "satli.star");

    public static bool RetireLegacyBridge(string steamDirectory, Func<bool>? steamRunning = null)
    {
        steamDirectory = SteamLocator.ValidateSteamDirectory(steamDirectory);
        var legacy = LegacyPluginPath(steamDirectory);
        FileOperations.EnsureNoReparsePoints(legacy);
        if (!File.Exists(legacy)) return false;
        if ((steamRunning ?? SteamLocator.IsSteamRunning)())
            throw new PreflightException("请先正常退出 Steam，再将旧 satli.star 移入回收站；快照、译文和备份会保留。");
        RecycleBin.FileIfExists(legacy);
        return true;
    }

    public static async Task<LitePluginInstallResult> InstallAsync(
        string steamDirectory, HttpClient client, CancellationToken cancellationToken = default,
        string? cacheDirectory = null, Action<string>? onProgress = null)
    {
        steamDirectory = SteamLocator.ValidateSteamDirectory(steamDirectory);
        if (!MillenniumInstallation.IsInstalled(steamDirectory))
            throw new PreflightException("尚未安装 Millennium，请使用 SATLI 的安装 / 管理入口完成安装。");
        var destination = PluginPath(steamDirectory);
        FileOperations.EnsureNoReparsePoints(destination);
        if (Directory.Exists(destination)) throw new PreflightException("插件目标路径已被目录占用，请检查 Steam 路径。");
        if (File.Exists(destination)) return new(destination, true); // lite owns its subsequent updates and private data.
        if (File.Exists(LegacyPluginPath(steamDirectory)))
            throw new PreflightException("发现旧 SATLI 显示桥。请退出 Steam 并确认移除旧桥后，再安装 lite。");
        var bytes = await OfficialReleaseDownloader.DownloadAsync("GaBoron/SATLI-lite", _ => "satli-lite.star",
            client, cacheDirectory, cancellationToken, onProgress, ComponentReleaseFallbacks.Lite).ConfigureAwait(false);
        FileOperations.EnsureNoReparsePoints(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            FileOperations.EnsureNoReparsePoints(destination);
            if (File.Exists(LegacyPluginPath(steamDirectory)))
                throw new PreflightException("下载期间出现旧显示桥，请先确认迁移；本次不会安装 lite。");
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { return new(destination, true); }
            return new(destination, false);
        }
        finally { RecycleBin.FileIfExists(temporary); }
    }
}
