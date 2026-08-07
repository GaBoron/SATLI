using Satli_Gui.Models;

namespace Satli_Gui.Services;

public sealed record DownloadSourceDefinition(
    string Id,
    string DisplayName,
    string Description,
    string Root)
{
    public Uri CatalogEndpoint => new($"{Root}/index.json");
}

public static class DownloadSourceCatalog
{
    private const string Repository = "GaBoron/steam-achievement-translation-library";

    public static readonly IReadOnlyList<DownloadSourceDefinition> Sources =
    [
        new(
            "jsdelivr",
            "jsDelivr",
            "多 CDN 分发；GitHub Raw 不可用时备用。",
            $"https://cdn.jsdelivr.net/gh/{Repository}@main"),
        new(
            "github",
            "GitHub Raw",
            "GitHub 官方原始文件；目录更新通常最直接。",
            $"https://raw.githubusercontent.com/{Repository}/main"),
        new(
            "jsdelivr-fastly",
            "jsDelivr · Fastly",
            "jsDelivr 的 Fastly 域名；主域名不可用时备用。",
            $"https://fastly.jsdelivr.net/gh/{Repository}@main"),
        new(
            "staticdelivr",
            "StaticDelivr",
            "面向 GitHub 开源文件的全球 CDN。",
            $"https://cdn.staticdelivr.com/gh/{Repository}/main"),
    ];

    private static readonly IReadOnlyDictionary<string, DownloadSourceDefinition> ById =
        Sources.ToDictionary(source => source.Id, StringComparer.Ordinal);

    public static DownloadSourceSettings Normalize(DownloadSourceSettings? settings) => new()
    {
        IndexSourceOrder = NormalizeOrder(
            settings?.IndexSourceOrder,
            DownloadSourceDefaults.IndexOrder),
        FileSourceOrder = NormalizeOrder(
            settings?.FileSourceOrder,
            DownloadSourceDefaults.FileOrder),
    };

    public static IReadOnlyList<DownloadSourceOption> Options(IEnumerable<string> sourceIds) =>
        sourceIds
            .Select(sourceId => ById[sourceId])
            .Select(source => new DownloadSourceOption(
                source.Id,
                source.DisplayName,
                source.Description))
            .ToList();

    public static IReadOnlyList<Uri> CatalogEndpoints(DownloadSourceSettings? settings)
    {
        var normalized = Normalize(settings);
        return normalized.IndexSourceOrder
            .Select(sourceId => ById[sourceId].CatalogEndpoint)
            .ToList();
    }

    public static string EnvironmentOrder(IEnumerable<string> sourceIds) =>
        string.Join(';', sourceIds);

    private static List<string> NormalizeOrder(
        IEnumerable<string>? requested,
        IReadOnlyList<string> defaultOrder)
    {
        var result = new List<string>();
        if (requested is not null)
        {
            foreach (var sourceId in requested)
            {
                var normalized = sourceId?.Trim().ToLowerInvariant();
                if (normalized is not null
                    && ById.ContainsKey(normalized)
                    && defaultOrder.Contains(normalized, StringComparer.Ordinal)
                    && !result.Contains(normalized, StringComparer.Ordinal))
                {
                    result.Add(normalized);
                }
            }
        }
        foreach (var sourceId in defaultOrder)
        {
            if (!result.Contains(sourceId, StringComparer.Ordinal))
            {
                result.Add(sourceId);
            }
        }
        return result;
    }
}
