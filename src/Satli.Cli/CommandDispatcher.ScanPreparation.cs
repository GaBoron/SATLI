using System.Diagnostics;
using System.Text.Json.Nodes;
using Satli.Core;
using Satli.Core.Models;
using Satli.Core.Steam;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private async Task<ScanPreparation> PrepareScanAsync(Arguments args, string scope)
    {
        var catalogTask = LoadScanCatalogAsync(args, scope);
        var discoveryTask = DiscoverSteamGamesAsync(args, scope);
        await Task.WhenAll(catalogTask, discoveryTask);

        var catalog = await catalogTask;
        var discovery = await discoveryTask;
        return new ScanPreparation(
            catalog.Catalog,
            catalog.Available,
            discovery.SteamDirectory,
            discovery.Games,
            scope != "cloud",
            catalog.ElapsedMilliseconds,
            discovery.ElapsedMilliseconds);
    }

    private async Task<CatalogPreparation> LoadScanCatalogAsync(Arguments args, string scope)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var catalog = await Repository(args).LoadAsync(
                args.Has("--offline") || args.Has("--catalog-cache-only"));
            return new CatalogPreparation(catalog, true, stopwatch.ElapsedMilliseconds);
        }
        catch (CatalogException exception) when (scope == "local")
        {
            _events.Emit("scan", "warning", new JsonObject
            {
                ["message"] = $"翻译目录不可用，已继续扫描本地游戏；收录状态暂时未知：{exception.Message}",
            });
            return new CatalogPreparation(
                new TranslationCatalog(0, new Dictionary<string, CatalogEntry>()),
                false,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static async Task<SteamDiscoveryPreparation> DiscoverSteamGamesAsync(
        Arguments args,
        string scope)
    {
        if (scope == "cloud")
        {
            return new SteamDiscoveryPreparation(
                null,
                new Dictionary<string, DiscoveryRecord>(),
                0);
        }

        var explicitSteamDirectory = args.SteamDirectory;
        var account = args.Value("--account");
        var stopwatch = Stopwatch.StartNew();
        return await Task.Run(() =>
        {
            var steamDirectory = SteamLocator.FindSteamDirectory(explicitSteamDirectory);
            var games = SteamLocator.DiscoverLocalGames(steamDirectory, account);
            return new SteamDiscoveryPreparation(
                steamDirectory,
                games,
                stopwatch.ElapsedMilliseconds);
        });
    }

    private sealed record CatalogPreparation(
        TranslationCatalog Catalog,
        bool Available,
        long ElapsedMilliseconds);

    private sealed record SteamDiscoveryPreparation(
        string? SteamDirectory,
        Dictionary<string, DiscoveryRecord> Games,
        long ElapsedMilliseconds);

    private sealed record ScanPreparation(
        TranslationCatalog Catalog,
        bool CatalogAvailable,
        string? SteamDirectory,
        Dictionary<string, DiscoveryRecord> Discovered,
        bool UsedParallelPreparation,
        long CatalogElapsedMilliseconds,
        long SteamDiscoveryElapsedMilliseconds);
}
