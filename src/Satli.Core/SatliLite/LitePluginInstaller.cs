using System.Text.Json.Nodes;
using System.Text.Json;
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
        string steamDirectory, HttpClient client, CancellationToken cancellationToken = default)
    {
        steamDirectory = SteamLocator.ValidateSteamDirectory(steamDirectory);
        if (!MillenniumInstallation.IsInstalled(steamDirectory))
            throw new PreflightException("请先按官方指南安装 Millennium");
        var destination = PluginPath(steamDirectory);
        FileOperations.EnsureNoReparsePoints(destination);
        if (Directory.Exists(destination)) throw new PreflightException("插件目标路径已被目录占用，请检查 Steam 路径。");
        if (File.Exists(destination)) return new(destination, true); // lite owns its subsequent updates and private data.
        if (File.Exists(LegacyPluginPath(steamDirectory)))
            throw new PreflightException("发现旧 SATLI 显示桥。请退出 Steam 并确认移除旧桥后，再安装 lite。");
        var bytes = await DownloadReleaseAsync(client, cancellationToken).ConfigureAwait(false);
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

    private static async Task<byte[]> DownloadReleaseAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/GaBoron/SATLI-lite/releases/latest");
            request.Headers.UserAgent.ParseAdd("SATLI/2.4.1");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var metadataBytes = await ReadLimitedAsync(response.Content, 1024 * 1024, deadline.Token).ConfigureAwait(false);
            var (size, digest, url) = ParseAsset(metadataBytes);
            using var download = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            download.EnsureSuccessStatusCode();
            var bytes = await ReadLimitedAsync(download.Content, (int)size, deadline.Token).ConfigureAwait(false);
            if (bytes.LongLength != size || !FileOperations.Sha256(bytes).Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                throw new IntegrityException("SATLI-lite 下载大小或完整性校验失败");
            return bytes;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PreflightException("SATLI-lite 下载中断或超时；未安装新插件，可稍后重试。", exception);
        }
    }

    private static (long Size, string Digest, string Url) ParseAsset(byte[] metadataBytes)
    {
        try
        {
            var release = JsonNode.Parse(metadataBytes)!.AsObject();
            if (release["draft"]?.GetValue<bool>() != false || release["prerelease"]?.GetValue<bool>() != false)
                throw new IntegrityException("SATLI-lite 下载来源不是正式 Release");
            var tag = release["tag_name"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(tag) || tag.Length > 128) throw new IntegrityException("正式 Release 缺少有效标签");
            var assets = release["assets"]!.AsArray().OfType<JsonObject>()
                .Where(value => value["name"]?.ToString() == "satli-lite.star").ToArray();
            if (assets.Length != 1) throw new IntegrityException("最新正式版未提供唯一的 satli-lite.star");
            var asset = assets[0];
            var size = asset["size"]?.GetValue<long>() ?? 0;
            var digest = asset["digest"]?.GetValue<string>() ?? "";
            var url = asset["browser_download_url"]?.GetValue<string>() ?? "";
            var expectedUrl = $"https://github.com/GaBoron/SATLI-lite/releases/download/{Uri.EscapeDataString(tag)}/satli-lite.star";
            if (asset["state"]?.ToString() != "uploaded" || size is < 1 or > 64 * 1024 * 1024
                || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71
                || !digest[7..].All(char.IsAsciiHexDigit) || url != expectedUrl)
                throw new IntegrityException("官方 Release 资产缺少可验证的下载元数据");
            return (size, digest, url);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or NullReferenceException)
        {
            throw new IntegrityException("官方 Release 元数据格式无效", exception);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit) throw new IntegrityException("下载内容超过安全上限");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + count > limit) throw new IntegrityException("下载内容超过安全上限");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
