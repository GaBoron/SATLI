using Satli.Core.Formats;
using Satli.Core.Models;

namespace Satli.Core.Catalog;

public sealed partial class CatalogRepository
{
    public async Task<string> DownloadJsonAsync(
        string appId,
        SchemaVariant variant,
        bool offline = false,
        CancellationToken cancellationToken = default)
    {
        var asset = variant.Json ?? throw new CatalogException("所选版本没有 JSON 资产");
        // Format 1 is identified by its catalog BIN source hash; format 2 uses its own hash.
        var key = asset.Sha256 ?? variant.Sha256;
        var destination = Path.Combine(_dataDirectory, "cache", "translations", $"v{asset.Version}-{key}.json");
        if (File.Exists(destination))
        {
            try
            {
                var cached = await ReadLimitedAsync(destination, TranslationJson.MaximumBytes, cancellationToken).ConfigureAwait(false);
                TranslationJson.Parse(cached, appId, variant);
                return destination;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or IntegrityException)
            {
                if (offline) throw new IntegrityException("离线 JSON 缓存未通过校验", exception);
            }
        }
        if (offline) throw new CatalogException("离线缓存中没有所选显示文本 JSON");
        var path = $"files/{appId}/{variant.VariantId}/UserGameStatsSchema_{appId}.json";
        var errors = new List<Exception>();
        foreach (var root in _fileRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var payload = await GetLimitedAsync(new Uri($"{root}/{EscapePath(path)}"),
                    TranslationJson.MaximumBytes, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                TranslationJson.Parse(payload, appId, variant);
                await WriteCacheAsync(destination, payload, cancellationToken);
                return destination;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or TaskCanceledException or IntegrityException)
            {
                errors.Add(exception);
            }
        }
        throw new CatalogException("无法下载通过校验的 JSON，请刷新目录后重试", errors.LastOrDefault());
    }
}
