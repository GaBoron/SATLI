using System.Net;
using System.Security.Cryptography;
using Satli.Core.FileSystem;
using Satli.Core.Models;

namespace Satli.Core.Catalog;

public sealed class CatalogRepository
{
    private readonly string _dataDirectory;
    private readonly HttpClient _client;
    private readonly IReadOnlyList<string> _catalogUrls;
    private readonly IReadOnlyList<string> _fileRoots;

    public CatalogRepository(
        string dataDirectory,
        HttpClient client,
        CatalogSourceOrder sourceOrder)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _client = client;
        _catalogUrls = sourceOrder.CatalogUrls;
        _fileRoots = sourceOrder.FileRoots;
    }

    public string CatalogCache => Path.Combine(_dataDirectory, "cache", "index.json");

    public async Task<TranslationCatalog> RefreshAsync(CancellationToken cancellationToken = default) =>
        await FetchCatalogAsync(true, cancellationToken);

    public async Task<TranslationCatalog> LoadAsync(
        bool offline = false,
        bool refresh = true,
        bool persist = true,
        CancellationToken cancellationToken = default)
    {
        CatalogException? networkError = null;
        if (!offline && refresh)
        {
            try
            {
                return await FetchCatalogAsync(persist, cancellationToken);
            }
            catch (CatalogException exception)
            {
                networkError = exception;
            }
        }
        if (File.Exists(CatalogCache))
        {
            try
            {
                var payload = await ReadLimitedAsync(
                    CatalogCache,
                    CatalogParser.MaximumCatalogBytes,
                    cancellationToken);
                var catalog = CatalogParser.Parse(payload, CatalogCache);
                return catalog with { FromCache = true };
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or CatalogException)
            {
                if (offline)
                {
                    throw new CatalogException($"缓存的翻译目录无效：{exception.Message}", exception);
                }
            }
        }
        if (networkError is not null)
        {
            throw networkError;
        }
        throw new CatalogException("离线模式下没有可用的翻译目录缓存");
    }

    public string SchemaCachePath(SchemaVariant variant) =>
        Path.Combine(_dataDirectory, "cache", "schemas", $"{variant.Sha256}.bin");

    public async Task<byte[]> ReadSchemaBytesAsync(
        SchemaVariant variant,
        bool offline = false,
        CancellationToken cancellationToken = default)
    {
        var cached = SchemaCachePath(variant);
        if (File.Exists(cached))
        {
            try
            {
                var payload = await ReadLimitedAsync(
                    cached,
                    CatalogParser.MaximumSchemaBytes,
                    cancellationToken);
                VerifySchemaBytes(payload, variant);
                return payload;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or IntegrityException)
            {
                if (offline)
                {
                    throw new CatalogException($"离线缓存中的 schema 无效：{variant.SchemaFile}", exception);
                }
            }
        }
        if (offline)
        {
            throw new CatalogException($"离线缓存中没有 {variant.SchemaFile}");
        }
        var failures = new List<Exception>();
        foreach (var root in _fileRoots)
        {
            try
            {
                var payload = await GetLimitedAsync(
                    new Uri($"{root}/{EscapePath(variant.SchemaFile)}"),
                    CatalogParser.MaximumSchemaBytes,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                VerifySchemaBytes(payload, variant);
                return payload;
            }
            catch (Exception exception) when (exception is HttpRequestException
                or TaskCanceledException
                or IntegrityException)
            {
                failures.Add(exception);
            }
        }
        if (failures.OfType<IntegrityException>().Any())
        {
            throw new IntegrityException("下载的翻译预览未通过完整性校验。请稍后重试。");
        }
        throw new CatalogException($"无法读取翻译预览：{failures.LastOrDefault()?.Message ?? "没有可用来源"}");
    }

    public async Task<string> DownloadSchemaAsync(
        SchemaVariant variant,
        bool offline = false,
        CancellationToken cancellationToken = default)
    {
        var destination = SchemaCachePath(variant);
        if (File.Exists(destination))
        {
            try
            {
                VerifySchemaFile(destination, variant);
                return destination;
            }
            catch (IntegrityException)
            {
                RecycleBin.FileIfExists(destination);
            }
        }
        if (offline)
        {
            throw new CatalogException($"离线缓存中没有 {variant.SchemaFile}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var failures = new List<Exception>();
        foreach (var root in _fileRoots)
        {
            var partial = Path.Combine(
                Path.GetDirectoryName(destination)!,
                $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.part");
            try
            {
                var payload = await GetLimitedAsync(
                    new Uri($"{root}/{EscapePath(variant.SchemaFile)}"),
                    CatalogParser.MaximumSchemaBytes,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                VerifySchemaBytes(payload, variant);
                await File.WriteAllBytesAsync(partial, payload, cancellationToken);
                RecycleBin.FileIfExists(destination);
                File.Move(partial, destination);
                return destination;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or HttpRequestException
                or TaskCanceledException
                or IntegrityException)
            {
                failures.Add(exception);
                RecycleBin.FileIfExists(partial);
            }
        }
        if (failures.OfType<IntegrityException>().Any())
        {
            throw new IntegrityException("下载的翻译文件未通过完整性校验。请稍后重试；如果问题持续，请报告此问题。");
        }
        throw new CatalogException($"无法下载翻译文件：{failures.LastOrDefault()?.Message ?? "没有可用来源"}");
    }

    public static void VerifySchemaFile(string path, SchemaVariant variant)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new IntegrityException($"无法读取下载文件：{path}");
        }
        if (info.Length > CatalogParser.MaximumSchemaBytes)
        {
            throw new IntegrityException($"文件超过 32 MiB 安全上限：{variant.SchemaFile}");
        }
        if (variant.FileSizeBytes is not null && info.Length != variant.FileSizeBytes)
        {
            throw new IntegrityException(
                $"文件大小不匹配：{variant.SchemaFile}，期望 {variant.FileSizeBytes}，实际 {info.Length}");
        }
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        VerifyHash(actual, variant);
    }

    public static void VerifySchemaBytes(ReadOnlySpan<byte> payload, SchemaVariant variant)
    {
        if (payload.Length > CatalogParser.MaximumSchemaBytes)
        {
            throw new IntegrityException($"文件超过 32 MiB 安全上限：{variant.SchemaFile}");
        }
        if (variant.FileSizeBytes is not null && payload.Length != variant.FileSizeBytes)
        {
            throw new IntegrityException(
                $"文件大小不匹配：{variant.SchemaFile}，期望 {variant.FileSizeBytes}，实际 {payload.Length}");
        }
        var actual = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        VerifyHash(actual, variant);
    }

    private async Task<TranslationCatalog> FetchCatalogAsync(
        bool persist,
        CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        foreach (var url in _catalogUrls)
        {
            try
            {
                var separator = url.Contains('?') ? '&' : '?';
                var requestUrl = new Uri($"{url}{separator}satli_refresh={Guid.NewGuid():N}");
                var payload = await GetLimitedAsync(
                    requestUrl,
                    CatalogParser.MaximumCatalogBytes,
                    TimeSpan.FromSeconds(15),
                    cancellationToken);
                var catalog = CatalogParser.Parse(payload, url);
                if (persist)
                {
                    await WriteCacheAsync(CatalogCache, payload, cancellationToken);
                }
                return catalog;
            }
            catch (Exception exception) when (exception is HttpRequestException
                or TaskCanceledException
                or CatalogException)
            {
                errors.Add(exception);
            }
        }
        var network = errors.LastOrDefault(exception => exception is not CatalogException);
        throw network is not null
            ? new CatalogException($"无法获取在线翻译目录：{network.Message}", network)
            : new CatalogException("在线翻译目录返回了无法识别的数据，请稍后重试或使用本地缓存。");
    }

    private async Task<byte[]> GetLimitedAsync(
        Uri uri,
        int maximumBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("SATLI/2.2.2 (+https://github.com/GaBoron/SATLI)");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
        };
        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linked.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new HttpRequestException($"{uri} 返回 404。", null, response.StatusCode);
        }
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new IntegrityException($"下载内容超过 {maximumBytes} 字节安全上限。");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, linked.Token);
            if (count == 0)
            {
                return output.ToArray();
            }
            if (output.Length + count > maximumBytes)
            {
                throw new IntegrityException($"下载内容超过 {maximumBytes} 字节安全上限。");
            }
            output.Write(buffer, 0, count);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
        {
            throw new IntegrityException($"文件超过 {maximumBytes} 字节安全上限：{path}");
        }
        using var output = new MemoryStream((int)stream.Length);
        await stream.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }

    private static async Task WriteCacheAsync(
        string destination,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.part");
        try
        {
            await File.WriteAllBytesAsync(partial, payload, cancellationToken);
            RecycleBin.FileIfExists(destination);
            File.Move(partial, destination);
        }
        catch
        {
            RecycleBin.FileIfExists(partial);
            throw;
        }
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static void VerifyHash(string actual, SchemaVariant variant)
    {
        if (!actual.Equals(variant.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IntegrityException(
                $"SHA-256 不匹配：{variant.SchemaFile}，期望 {variant.Sha256}，实际 {actual}");
        }
    }
}
