using System.IO.Compression;
using Satli.Core.FileSystem;
using Satli.Core.Steam;

namespace Satli.Core.SatliLite;

/// <summary>Installs the official Windows payload without running a second network-dependent installer.</summary>
public static class MillenniumInstaller
{
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    public static async Task<bool> EnsureInstalledAsync(string steamDirectory, HttpClient client,
        string? cacheDirectory = null, CancellationToken cancellationToken = default, Func<bool>? steamRunning = null,
        Action<string>? onProgress = null)
    {
        steamDirectory = Path.TrimEndingDirectorySeparator(SteamLocator.ValidateSteamDirectory(steamDirectory));
        await InstallGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (MillenniumInstallation.IsInstalled(steamDirectory)) return false;
            RequireSteamStopped(steamRunning);
            var payload = await OfficialReleaseDownloader.DownloadAsync("SteamClientHomebrew/Millennium", tag =>
            {
                if (!Version.TryParse(tag.TrimStart('v'), out var version) || version < new Version(3, 5, 0))
                    throw new IntegrityException("SATLI-lite 需要 Millennium 3.5.0 或更高版本。");
                return $"millennium-{tag}-windows-x86_64.zip";
            }, client, cacheDirectory, cancellationToken, onProgress, ComponentReleaseFallbacks.Millennium).ConfigureAwait(false);
            // Steam may have started, or another installer may have completed, during the download.
            if (MillenniumInstallation.IsInstalled(steamDirectory)) return false;
            RequireSteamStopped(steamRunning);
            onProgress?.Invoke("正在安装已校验的 Millennium 组件。");
            InstallArchive(payload, steamDirectory, cancellationToken, steamRunning);
            return true;
        }
        finally { InstallGate.Release(); }
    }

    private static void RequireSteamStopped(Func<bool>? steamRunning)
    {
        if ((steamRunning ?? SteamLocator.IsSteamRunning)())
            throw new PreflightException("请先正常退出 Steam，再安装 Millennium。");
    }

    private static void InstallArchive(byte[] payload, string steam, CancellationToken cancellationToken, Func<bool>? steamRunning)
    {
        using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);
        if (archive.Entries.Count is < 1 or > 512) throw new IntegrityException("Millennium 安装包文件数量无效。");
        var files = new List<(ZipArchiveEntry Entry, string Relative, string Destination)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/');
            var parts = relative.TrimEnd('/').Split('/');
            if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."
                || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                || !names.Add(relative.TrimEnd('/')) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new IntegrityException("Millennium 安装包包含不安全或重复路径。");
            if (relative != "wsock32.dll" && relative != "millennium/"
                && !relative.StartsWith("millennium/lib/", StringComparison.Ordinal)
                && !relative.StartsWith("millennium/bin/", StringComparison.Ordinal))
                throw new IntegrityException("Millennium 安装包包含不支持的文件路径，请使用官方安装器检查。");
            if (relative.EndsWith('/')) continue;
            total = checked(total + entry.Length);
            if (entry.Length < 1 || total > 256 * 1024 * 1024) throw new IntegrityException("Millennium 解压大小无效。");
            var destination = Path.GetFullPath(Path.Combine(steam, relative));
            if (!destination.StartsWith(steam + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IntegrityException("Millennium 文件路径超出 Steam 目录。");
            FileOperations.EnsureNoReparsePoints(destination);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new PreflightException($"Millennium 安装位置已有文件：{relative}。为保留现有组件，本次未覆盖，请检查安装状态。");
            files.Add((entry, relative, destination));
        }
        if (!files.Any(file => file.Relative == "wsock32.dll")
            || !files.Any(file => file.Relative == "millennium/lib/millennium.dll"))
            throw new IntegrityException("Millennium 安装包缺少启动或核心组件。");

        var staging = Path.Combine(steam, ".satli-millennium-" + Guid.NewGuid().ToString("N"));
        FileOperations.EnsureNoReparsePoints(staging);
        var committed = new List<string>();
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var staged = Path.Combine(staging, file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                using var source = file.Entry.Open();
                using var target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(target);
                if (target.Length != file.Entry.Length) throw new IntegrityException("Millennium 文件解压不完整。");
                target.Flush(true);
            }
            // Publish the detection marker last, so a failed installation never appears complete.
            foreach (var file in files.OrderBy(file => file.Relative == "millennium/lib/millennium.dll"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireSteamStopped(steamRunning);
                FileOperations.EnsureNoReparsePoints(file.Destination);
                Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
                File.Move(Path.Combine(staging, file.Relative), file.Destination);
                committed.Add(file.Destination);
            }
        }
        catch
        {
            foreach (var path in committed.AsEnumerable().Reverse()) RecycleBin.FileIfExists(path);
            throw;
        }
        finally { RecycleBin.DirectoryIfExists(staging); }
    }
}
