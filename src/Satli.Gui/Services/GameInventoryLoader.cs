using System.Diagnostics;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

internal interface IGameInventoryLoader
{
    Task<GameInventorySnapshot> LoadAsync(
        GameInventoryScope scope,
        GuiSettings settings,
        Action<SatliEvent>? onEvent = null,
        bool useCatalogCache = false);
}

internal sealed record GameInventorySnapshot(
    GameInventoryScope Scope,
    IReadOnlyList<GameItem> Games,
    IReadOnlyList<SatliEvent> Events,
    string ConfigurationWarning,
    long ElapsedMilliseconds);

internal sealed class GameInventoryLoader : IGameInventoryLoader
{
    private readonly SatliCliService _cli;

    public GameInventoryLoader(SatliCliService? cli = null)
    {
        _cli = cli ?? new SatliCliService();
    }

    public async Task<GameInventorySnapshot> LoadAsync(
        GameInventoryScope scope,
        GuiSettings settings,
        Action<SatliEvent>? onEvent = null,
        bool useCatalogCache = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var arguments = BuildArguments(
            scope,
            settings,
            useCatalogCache,
            out var warning);
        var result = await _cli.RunAsync(
            arguments,
            onEvent,
            networkSettings: settings.Network,
            steamLibrarySettings: settings.SteamLibrary,
            downloadSourceSettings: settings.DownloadSources);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        var games = result.Events
            .Where(item => item.Event == "item-succeeded")
            .Select(item => GameItem.FromPayload(item.Payload))
            .ToArray();
        return new GameInventorySnapshot(
            scope,
            games,
            result.Events,
            warning ?? string.Empty,
            stopwatch.ElapsedMilliseconds);
    }

    private static List<string> BuildArguments(
        GameInventoryScope scope,
        GuiSettings settings,
        bool useCatalogCache,
        out string? warning)
    {
        var arguments = new List<string>
        {
            "scan",
            "--scope",
            scope == GameInventoryScope.Local ? "local" : "cloud",
            "--jsonl",
        };
        if (!string.IsNullOrWhiteSpace(settings.DataDirectory))
        {
            arguments.AddRange(["--data-dir", settings.DataDirectory]);
        }
        if (scope == GameInventoryScope.Local
            && !string.IsNullOrWhiteSpace(settings.SteamDirectory))
        {
            arguments.AddRange(["--steam-dir", settings.SteamDirectory]);
        }
        if (settings.Offline)
        {
            arguments.Add("--offline");
        }
        else if (useCatalogCache)
        {
            arguments.Add("--catalog-cache-only");
        }
        warning = scope == GameInventoryScope.Local
            ? SteamLibraryCliOptions.AppendScanArguments(arguments, settings)
            : null;
        return arguments;
    }
}
