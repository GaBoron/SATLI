using System.Text.Json.Nodes;
using Satli.Core.FileSystem;

namespace Satli.Core.SatliLite;

public static class LitePluginInstaller
{
    public static async Task InstallAsync(string steamDirectory, HttpClient client, CancellationToken cancellationToken = default)
    {
        if (!MillenniumInstallation.IsInstalled(steamDirectory))
            throw new PreflightException("请先按官方指南安装 Millennium");
        var destination = Path.Combine(Path.GetFullPath(steamDirectory), "millennium", "plugins", "satli-lite.star");
        if (File.Exists(destination)) return; // lite owns its subsequent updates and private data.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/GaBoron/SATLI-lite/releases/latest");
        request.Headers.UserAgent.ParseAdd("SATLI/2.4.1");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        var metadataBytes = await ReadLimitedAsync(response.Content, 1024 * 1024, deadline.Token);
        var release = JsonNode.Parse(metadataBytes)!.AsObject();
        var asset = release["assets"]!.AsArray().OfType<JsonObject>()
            .SingleOrDefault(value => value["name"]?.ToString() == "satli-lite.star")
            ?? throw new IntegrityException("最新正式版未提供 satli-lite.star");
        var size = asset["size"]?.GetValue<long>() ?? 0;
        var digest = asset["digest"]?.ToString() ?? "";
        var url = asset["browser_download_url"]?.ToString() ?? "";
        if (size is < 1 or > 64 * 1024 * 1024 || !digest.StartsWith("sha256:", StringComparison.Ordinal)
            || digest.Length != 71 || !digest[7..].All(char.IsAsciiHexDigit)
            || !url.StartsWith("https://github.com/GaBoron/SATLI-lite/releases/download/", StringComparison.Ordinal)
            || !url.EndsWith("/satli-lite.star", StringComparison.Ordinal))
            throw new IntegrityException("官方 Release 资产缺少可验证的下载元数据");
        using var download = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        download.EnsureSuccessStatusCode();
        var bytes = await ReadLimitedAsync(download.Content, 64 * 1024 * 1024, deadline.Token);
        if (bytes.LongLength != size || !FileOperations.Sha256(bytes).Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new IntegrityException("SATLI-lite 下载大小或完整性校验失败");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, deadline.Token);
            File.Move(temporary, destination);
        }
        finally { RecycleBin.FileIfExists(temporary); }
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
