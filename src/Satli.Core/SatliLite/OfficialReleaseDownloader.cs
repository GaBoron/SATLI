using System.Net;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;

namespace Satli.Core.SatliLite;

internal sealed record OfficialReleaseAsset(string Tag, string Name, long Size, string Digest, string Url);

/// <summary>Downloads verified official release assets, reusing completed downloads after interrupted setup.</summary>
internal static class OfficialReleaseDownloader
{
    public static async Task<byte[]> DownloadAsync(string repository, Func<string, string> assetName,
        HttpClient client, string? cacheDirectory, CancellationToken cancellationToken, Action<string>? onProgress = null,
        OfficialReleaseAsset? fallback = null)
    {
        var cache = cacheDirectory is null ? null : Path.Combine(Path.GetFullPath(cacheDirectory),
            "component-downloads", repository.Replace('/', '-'));
        var metadataPath = cache is null ? null : Path.Combine(cache, "release.json");
        if (metadataPath is not null && File.Exists(metadataPath))
        {
            FileOperations.EnsureNoReparsePoints(metadataPath);
            if (new FileInfo(metadataPath).Length <= 1024 * 1024)
            {
                try
                {
                    var cached = Parse(await File.ReadAllBytesAsync(metadataPath, cancellationToken), repository, assetName);
                    var cachedPath = Path.Combine(cache!, cached.Name);
                    FileOperations.EnsureNoReparsePoints(cachedPath);
                    if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length == cached.Size)
                    {
                        var bytes = await File.ReadAllBytesAsync(cachedPath, cancellationToken);
                        if (Matches(bytes, cached))
                        {
                            onProgress?.Invoke($"已校验并复用组件缓存：{cached.Name}。");
                            return bytes;
                        }
                    }
                }
                catch (IntegrityException) { /* An incomplete or outdated cache is replaced by a verified download. */ }
            }
        }

        onProgress?.Invoke($"正在获取 {repository} 的官方发布信息。");
        byte[] metadata;
        try
        {
            metadata = await GetAsync(client, $"https://api.github.com/repos/{repository}/releases/latest",
                1024 * 1024, cancellationToken, onProgress);
        }
        catch (Exception exception) when (fallback is not null && !cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or PreflightException)
        {
            onProgress?.Invoke($"GitHub 发布信息暂不可用，改用已核实的官方 {fallback.Tag} 安装包。");
            metadata = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                tag_name = fallback.Tag, draft = false, prerelease = false,
                assets = new[] { new { name = fallback.Name, size = fallback.Size, digest = "sha256:" + fallback.Digest,
                    state = "uploaded", browser_download_url = fallback.Url } },
            });
        }
        var asset = Parse(metadata, repository, assetName);
        onProgress?.Invoke($"正在下载并校验：{asset.Name}。");
        var payload = await GetAsync(client, asset.Url, (int)asset.Size, cancellationToken, onProgress);
        if (!Matches(payload, asset)) throw new IntegrityException("官方组件下载大小或完整性校验失败，请重试。");
        if (cache is not null)
        {
            var path = Path.Combine(cache, asset.Name);
            FileOperations.EnsureNoReparsePoints(path);
            FileOperations.WriteDurable(path, payload);
            FileOperations.WriteDurable(metadataPath!, metadata);
        }
        return payload;
    }

    private static bool Matches(byte[] bytes, OfficialReleaseAsset asset) =>
        bytes.LongLength == asset.Size && FileOperations.Sha256(bytes).Equals(asset.Digest, StringComparison.OrdinalIgnoreCase);

    private static OfficialReleaseAsset Parse(byte[] metadata, string repository, Func<string, string> assetName)
    {
        try
        {
            var release = JsonNode.Parse(metadata)!.AsObject();
            var tag = release["tag_name"]?.GetValue<string>() ?? "";
            if (release["draft"]?.GetValue<bool>() != false || release["prerelease"]?.GetValue<bool>() != false
                || tag.Length is < 1 or > 128)
                throw new IntegrityException("组件下载来源不是有效的官方正式 Release。");
            var name = assetName(tag);
            if (Path.GetFileName(name) != name || name.Contains('/') || name.Contains('\\'))
                throw new IntegrityException("组件文件名无效。");
            var assets = release["assets"]!.AsArray().OfType<JsonObject>()
                .Where(value => value["name"]?.ToString() == name).ToArray();
            if (assets.Length != 1) throw new IntegrityException($"官方正式版未提供唯一的 {name}。");
            var asset = assets[0];
            var size = asset["size"]?.GetValue<long>() ?? 0;
            var digest = asset["digest"]?.GetValue<string>() ?? "";
            var url = asset["browser_download_url"]?.GetValue<string>() ?? "";
            var expected = $"https://github.com/{repository}/releases/download/{Uri.EscapeDataString(tag)}/{name}";
            if (asset["state"]?.ToString() != "uploaded" || size is < 1 or > 64 * 1024 * 1024
                || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71
                || !digest[7..].All(char.IsAsciiHexDigit) || url != expected)
                throw new IntegrityException("官方 Release 资产缺少可验证的下载元数据。");
            return new(tag, name, size, digest[7..], url);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException
            or FormatException or NullReferenceException)
        {
            throw new IntegrityException("官方 Release 元数据格式无效。", exception);
        }
    }

    private static async Task<byte[]> GetAsync(HttpClient client, string url, int limit,
        CancellationToken cancellationToken, Action<string>? onProgress)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("SATLI/4.0.0");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > limit) throw new IntegrityException("下载内容超过安全上限。");
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var output = new MemoryStream();
                var buffer = new byte[65536];
                int count;
                while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0)
                {
                    if (output.Length + count > limit) throw new IntegrityException("下载内容超过安全上限。");
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or IOException or OperationCanceledException)
            {
                if (exception is HttpRequestException { StatusCode: { } status }
                    && status != HttpStatusCode.RequestTimeout && status != HttpStatusCode.TooManyRequests && (int)status < 500)
                    throw;
                if (attempt >= 2) throw new PreflightException("官方组件下载中断或超时。请检查网络、代理或 DNS 后重试，已校验的下载会保留。", exception);
                onProgress?.Invoke($"组件下载中断，正在重试（{attempt + 2}/3）。");
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
