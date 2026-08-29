namespace Satli.Core.Catalog;

public sealed record CatalogSource(string Id, string Root)
{
    public IReadOnlyList<string> CatalogUrls =>
        [$"{Root}/index-v2.json", $"{Root}/index.json"];
}

public sealed record CatalogSourceOrder(
    IReadOnlyList<string> IndexSourceIds,
    IReadOnlyList<string> FileSourceIds)
{
    public IReadOnlyList<string> CatalogUrls => IndexSourceIds
        .SelectMany(id => CatalogSourceCatalog.Sources[id].CatalogUrls)
        .ToArray();

    public IReadOnlyList<string> FileRoots => FileSourceIds
        .Select(id => CatalogSourceCatalog.Sources[id].Root)
        .ToArray();
}

public static class CatalogSourceCatalog
{
    private const string Repository = "GaBoron/steam-achievement-translation-library";

    public static IReadOnlyDictionary<string, CatalogSource> Sources { get; } =
        new Dictionary<string, CatalogSource>(StringComparer.OrdinalIgnoreCase)
        {
            ["jsdelivr"] = new("jsdelivr", $"https://cdn.jsdelivr.net/gh/{Repository}@main"),
            ["github"] = new("github", $"https://raw.githubusercontent.com/{Repository}/main"),
            ["jsdelivr-fastly"] = new(
                "jsdelivr-fastly",
                $"https://fastly.jsdelivr.net/gh/{Repository}@main"),
            ["staticdelivr"] = new(
                "staticdelivr",
                $"https://cdn.staticdelivr.com/gh/{Repository}/main"),
        };

    public static IReadOnlyList<string> DefaultIndexSourceIds { get; } =
        ["github", "jsdelivr", "jsdelivr-fastly", "staticdelivr"];

    public static IReadOnlyList<string> DefaultFileSourceIds { get; } =
        ["jsdelivr", "jsdelivr-fastly", "github"];

    public static CatalogSourceOrder FromEnvironment(IReadOnlyDictionary<string, string> environment) =>
        new(
            ParseOrder(
                environment.GetValueOrDefault("SATLI_INDEX_SOURCES"),
                DefaultIndexSourceIds,
                "目录下载源"),
            ParseOrder(
                environment.GetValueOrDefault("SATLI_FILE_SOURCES"),
                DefaultFileSourceIds,
                "文件下载源"));

    private static IReadOnlyList<string> ParseOrder(
        string? raw,
        IReadOnlyList<string> defaults,
        string description)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaults.ToArray();
        }
        var result = new List<string>();
        foreach (var value in raw.Replace(',', ';').Split(';'))
        {
            var id = value.Trim().ToLowerInvariant();
            if (id.Length == 0)
            {
                continue;
            }
            if (!Sources.ContainsKey(id))
            {
                throw new CatalogException($"{description}包含未知来源：{id}");
            }
            if (!defaults.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                throw new CatalogException($"{description}不支持来源：{id}");
            }
            if (!result.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(id);
            }
        }
        return result.Count > 0
            ? result
            : throw new CatalogException($"{description}不能为空。");
    }
}
