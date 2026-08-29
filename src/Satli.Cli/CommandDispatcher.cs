using System.Text.Json.Nodes;
using Satli.Core;
using Satli.Core.Catalog;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Satli.Core.Imports;
using Satli.Core.Models;
using Satli.Core.Networking;
using Satli.Core.SchemaEditing;
using Satli.Core.State;
using Satli.Core.Steam;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private readonly EventSink _events;
    private readonly TextWriter _output;
    private readonly IReadOnlyDictionary<string, string> _environment;
    private readonly SchemaEditor _editor = new();
    public CommandDispatcher(
        EventSink events,
        TextWriter output,
        IReadOnlyDictionary<string, string> environment)
    {
        _events = events;
        _output = output;
        _environment = environment;
    }

    public static string Operation(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return "unknown";
        return args[0] switch
        {
            "cache" => "cache-refresh",
            "petition" => "petition-export",
            "protect" => $"protect-{(args.Count > 1 ? args[1] : "unknown")}",
            "schema" when args.Count > 2 && args[1] == "revisions" => $"schema-revisions-{args[2]}",
            "schema" => $"schema-{(args.Count > 1 ? args[1] : "unknown")}",
            _ => args[0],
        };
    }

    public async Task<int> RunAsync(string[] raw)
    {
        var args = new Arguments(raw);
        return raw[0] switch
        {
            "scan" => await Scan(args),
            "install" => await Install(args),
            "local-import" => await LocalImport(args),
            "status" => await Status(args),
            "restore" => Restore(args),
            "protect" => Protect(raw, args),
            "cache" => await Cache(args),
            "petition" => Petition(args),
            "schema" => Schema(raw, args),
            _ => throw new UsageException($"未知命令：{raw[0]}"),
        };
    }

    private async Task<int> Scan(Arguments args)
    {
        if (args.Has("--json") && _events.JsonLines)
            throw new UsageException("--json 与 --jsonl 不能同时使用");
        var scope = args.Value("--scope") ?? "manageable";
        if (scope is not ("manageable" or "local" or "cloud"))
            throw new UsageException($"无效扫描范围：{scope}");
        var preparation = await PrepareScanAsync(args, scope);
        var catalog = preparation.Catalog;
        var available = preparation.CatalogAvailable;
        if (catalog.Version == 1) WarnV1("scan");
        var steam = preparation.SteamDirectory;
        var discovered = preparation.Discovered;
        using var webClient = NetworkClient();
        if (steam is not null && args.Has("--include-owned-games"))
        {
            if (args.Has("--offline"))
            {
                _events.Emit("scan", "warning", new JsonObject
                {
                    ["message"] = "离线模式已启用，已跳过 Steam Web API 游戏库补全。",
                });
            }
            else
            {
                var steamId = args.Value("--owned-account")
                    ?? SteamLocator.DiscoverAccounts(steam)
                        .FirstOrDefault(account => account.MostRecent)?.SteamId;
                var apiKey = EnvironmentValue("SATLI_STEAM_WEB_API_KEY");
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    _events.Emit("scan", "warning", new JsonObject
                    {
                        ["message"] = "未填写 SteamID64，且本机未找到最近登录账号；已继续使用本地扫描结果。",
                    });
                }
                else if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _events.Emit("scan", "warning", new JsonObject
                    {
                        ["message"] = "Steam Web API Key 未配置，已继续使用本地扫描结果。",
                    });
                }
                else
                {
                    try
                    {
                        var owned = await SteamWebServices.GetOwnedGamesAsync(
                            webClient,
                            apiKey,
                            steamId);
                        SteamWebServices.MergeOwnedGames(discovered, owned, steamId);
                    }
                    catch (Exception exception) when (
                        exception is SatliException or HttpRequestException)
                    {
                        _events.Emit("scan", "warning", new JsonObject
                        {
                            ["message"] = $"Steam 游戏库补全失败，已继续使用本地扫描结果：{exception.Message}",
                        });
                    }
                }
            }
        }
        var registry = new ManagedGameRegistry(args.DataDirectory);
        var managedIds = registry.ManagedAppIds().ToHashSet(StringComparer.Ordinal);
        var ids = scope switch
        {
            "cloud" => catalog.Entries.Keys,
            "local" => discovered.Keys,
            _ => discovered.Keys.Intersect(catalog.Entries.Keys),
        };
        var ordered = ids.OrderBy(value => ulong.Parse(value)).ToArray();
        var missingNames = ordered.Where(id =>
                !catalog.Entries.ContainsKey(id)
                && (!discovered.TryGetValue(id, out var record)
                    || record.GameName.Length == 0))
            .ToArray();
        var resolvedNames = !args.Has("--offline") && missingNames.Length > 0
            ? await SteamWebServices.ResolveNamesAsync(
                webClient,
                args.DataDirectory,
                missingNames,
                (current, total, id) => _events.Emit("scan", "progress", new JsonObject
                {
                    ["phase"] = "name-lookup",
                    ["current"] = current,
                    ["total"] = total,
                    ["message"] = $"正在联网查询游戏名称 {current}/{total}（App ID {id}）",
                }))
            : new Dictionary<string, string>();
        _events.Emit("scan", "plan", new JsonObject
        {
            ["steam_dir"] = steam ?? "",
            ["scope"] = scope,
            ["count"] = ordered.Length,
            ["catalog_available"] = available,
            ["catalog_version"] = catalog.Version,
            ["catalog_from_cache"] = catalog.FromCache,
            ["catalog_source"] = catalog.Source,
            ["parallel_preparation"] = preparation.UsedParallelPreparation,
            ["catalog_elapsed_ms"] = preparation.CatalogElapsedMilliseconds,
            ["steam_discovery_elapsed_ms"] = preparation.SteamDiscoveryElapsedMilliseconds,
        });
        if (ordered.Length > 0)
        {
            _events.Emit("scan", "progress", new JsonObject
            {
                ["phase"] = "game-loading",
                ["current"] = 0,
                ["total"] = ordered.Length,
                ["message"] = $"正在加载游戏 0/{ordered.Length}",
            });
        }
        var outputRecords = new JsonArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var id = ordered[index];
            catalog.Entries.TryGetValue(id, out var entry);
            discovered.TryGetValue(id, out var discovery);
            var managed = managedIds.Contains(id)
                ? registry.Record(id)
                : new ManagedRecord(id, "unmanaged", null, null, null, null, null, false);
            var native = steam is not null && managed.InstalledState is "unmanaged" or "restored"
                ? SteamLocator.DetectAchievementLanguages(SteamLocator.SchemaTarget(steam, id))
                : [];
            var record = entry is null
                ? UnlistedRecord(id, discovery, managed, native, available)
                : GameRecord(entry, discovery?.Discovery ?? [], managed, native);
            if (entry is null
                && resolvedNames.TryGetValue(id, out var resolvedName)
                && resolvedName.Length > 0)
                record["game_name"] = resolvedName;
            outputRecords.Add(record.DeepClone());
            record["position"] = index + 1;
            _events.Emit("scan", "item-succeeded", record);
        }
        _events.Emit("scan", "completed", new JsonObject
        {
            ["count"] = ordered.Length,
            ["exit_code"] = 0,
        });
        if (args.Has("--json"))
            _output.WriteLine(outputRecords.ToJsonString());
        else if (!_events.JsonLines)
            foreach (var record in outputRecords.OfType<JsonObject>())
                _output.WriteLine(
                    $"{record["app_id"]?.GetValue<string>(),10}  "
                    + $"{record["catalog_status"]?.GetValue<string>(),-16} "
                    + $"{record["installed_state"]?.GetValue<string>(),-10} "
                    + record["game_name"]?.GetValue<string>());
        return 0;
    }

    private async Task<int> Status(Arguments args)
    {
        if (args.Has("--json") && _events.JsonLines)
            throw new UsageException("--json 与 --jsonl 不能同时使用");
        var registry = new ManagedGameRegistry(args.DataDirectory);
        var ids = args.Positionals(1, "--data-dir").ToArray();
        if (ids.Length == 0) ids = registry.ManagedAppIds().ToArray();
        TranslationCatalog? catalog = null;
        try
        {
            catalog = await Repository(args).LoadAsync(args.Has("--offline"));
            if (catalog.Version == 1) WarnV1("status");
        }
        catch (CatalogException)
        {
        }
        _events.Emit("status", "plan", new JsonObject
        {
            ["count"] = ids.Length,
            ["catalog_available"] = catalog is not null,
            ["catalog_version"] = catalog?.Version ?? 0,
            ["catalog_from_cache"] = catalog?.FromCache ?? false,
            ["catalog_source"] = catalog?.Source ?? "",
        });
        var outputRecords = new JsonArray();
        foreach (var id in ids)
        {
            var managed = registry.Record(id);
            var record = catalog?.Entries.TryGetValue(id, out var entry) == true
                ? GameRecord(entry, [], managed, [])
                : UnlistedRecord(id, null, managed, [], false);
            outputRecords.Add(record.DeepClone());
            _events.Emit("status", "item-succeeded", record);
        }
        _events.Emit("status", "completed", new JsonObject
        {
            ["count"] = ids.Length,
            ["exit_code"] = 0,
        });
        if (args.Has("--json"))
            _output.WriteLine(outputRecords.ToJsonString());
        else if (!_events.JsonLines)
            foreach (var record in outputRecords.OfType<JsonObject>())
                _output.WriteLine(
                    $"{record["app_id"]?.GetValue<string>(),10}  "
                    + $"{record["installed_state"]?.GetValue<string>(),-10} "
                    + record["game_name"]?.GetValue<string>());
        return 0;
    }

    private async Task<int> Cache(Arguments args)
    {
        var catalog = await Repository(args).RefreshAsync();
        if (catalog.Version == 1) WarnV1("cache-refresh");
        _events.Emit("cache-refresh", "completed", new JsonObject
        {
            ["count"] = catalog.Entries.Count,
            ["catalog_version"] = catalog.Version,
            ["source"] = catalog.Source,
            ["exit_code"] = 0,
        });
        return 0;
    }

    private CatalogRepository Repository(Arguments args)
    {
        return new CatalogRepository(
            args.DataDirectory,
            NetworkHttpClientFactory.Create(_environment),
            CatalogSourceCatalog.FromEnvironment(_environment));
    }

    private HttpClient NetworkClient() => NetworkHttpClientFactory.Create(_environment);

    private string? EnvironmentValue(string name) =>
        _environment.TryGetValue(name, out var value) ? value : null;

    private void WarnV1(string operation) =>
        _events.Emit(operation, "warning", new JsonObject
        {
            ["message"] = "当前下载源仅提供 V1 兼容目录；已自动回退，建议稍后刷新。",
        });

    private static JsonObject GameRecord(
        CatalogEntry entry,
        IEnumerable<string> discovery,
        ManagedRecord managed,
        IReadOnlyList<string> native)
    {
        var variants = new JsonArray(entry.Variants.Select(variant => (JsonNode)new JsonObject
        {
            ["variant_id"] = variant.VariantId,
            ["primary"] = variant.Primary,
            ["schema_file"] = variant.SchemaFile,
            ["sha256"] = variant.Sha256,
            ["file_size_bytes"] = variant.FileSizeBytes,
            ["note_zh"] = variant.NoteZh,
            ["note_en"] = variant.NoteEn,
            ["achievement_count"] = variant.AchievementCount,
        }).ToArray());
        return new JsonObject
        {
            ["app_id"] = entry.AppId,
            ["game_name"] = entry.GameName,
            ["discovery"] = Strings(discovery.Order()),
            ["catalog_status"] = entry.Status,
            ["contributors"] = Strings(entry.Contributors),
            ["variants"] = variants,
            ["installed_state"] = managed.InstalledState,
            ["installed_variant_id"] = managed.InstalledVariantId,
            ["installed_source"] = managed.InstalledSource,
            ["installed_at"] = managed.InstalledAt,
            ["installed_sha256"] = managed.InstalledSha256,
            ["native_languages"] = Strings(native),
            ["file_read_only"] = managed.FileReadOnly,
            ["action"] = "available",
            ["error"] = null,
        };
    }

    private static JsonObject UnlistedRecord(
        string id,
        DiscoveryRecord? discovery,
        ManagedRecord managed,
        IReadOnlyList<string> native,
        bool catalogAvailable) => new()
    {
        ["app_id"] = id,
        ["game_name"] = discovery?.GameName is { Length: > 0 } name
            ? name
            : managed.GameName ?? $"Steam 游戏 {id}",
        ["discovery"] = Strings(
            discovery is null ? Array.Empty<string>() : discovery.Discovery.Order()),
        ["catalog_status"] = catalogAvailable ? "unlisted" : "unknown",
        ["contributors"] = new JsonArray(),
        ["variants"] = new JsonArray(),
        ["installed_state"] = managed.InstalledState,
        ["installed_variant_id"] = managed.InstalledVariantId,
        ["installed_source"] = managed.InstalledSource,
        ["installed_at"] = managed.InstalledAt,
        ["installed_sha256"] = managed.InstalledSha256,
        ["native_languages"] = Strings(native),
        ["file_read_only"] = managed.FileReadOnly,
        ["action"] = "unavailable",
        ["error"] = null,
    };

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray());
}
