using System.Text.Json.Nodes;
using Satli.Core;
using Satli.Core.Formats;
using Satli.Core.Imports;
using Satli.Core.Models;
using Satli.Core.Steam;
using Satli.Core.Transactions;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private async Task<int> Install(Arguments args)
    {
        var dryRun = args.Has("--dry-run");
        var preview = args.Has("--preview-content");
        if (preview && (!dryRun || !_events.JsonLines))
            throw new UsageException("--preview-content 必须与 --dry-run --jsonl 一起使用");
        var repository = Repository(args);
        var catalog = await repository.LoadAsync(args.Has("--offline"), persist: !dryRun);
        if (catalog.Version == 1) WarnV1("install");
        var positionals = args.Positionals(
            1,
            "--data-dir",
            "--steam-dir",
            "--account",
            "--variant");
        if (positionals.Count == 0 && !args.Has("--matched"))
            throw new UsageException("请指定 APP_ID，或使用 --matched");
        var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
        var ids = args.Has("--matched")
            ? SteamLocator.DiscoverLocalGames(steam, args.Value("--account"))
                .Keys.Intersect(catalog.Entries.Keys).ToArray()
            : positionals.ToArray();
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in args.Values("--variant"))
        {
            var parts = value.Split('=', 2);
            if (parts.Length != 2 || !parts[0].All(char.IsAsciiDigit) || parts[1].Length == 0)
                throw new UsageException($"--variant 必须使用 APP_ID=VARIANT：{value}");
            if (!overrides.TryAdd(parts[0], parts[1]))
                throw new UsageException($"重复指定版本：{parts[0]}");
        }
        var plan = new List<(CatalogEntry Entry, SchemaVariant Variant)>();
        foreach (var id in ids.Distinct().OrderBy(value => ulong.Parse(value)))
        {
            if (!catalog.Entries.TryGetValue(id, out var entry))
                throw new UsageException($"翻译库中没有 App ID：{id}");
            if (entry.Status != "current" && !args.Has("--allow-outdated"))
                throw new UsageException($"{id} 不是 current，需显式使用 --allow-outdated");
            SchemaVariant variant;
            try
            {
                variant = overrides.TryGetValue(id, out var variantId)
                    ? entry.Variant(variantId)
                    : entry.PrimaryVariant();
            }
            catch (InvalidOperationException)
            {
                throw new UsageException($"{id} 没有指定版本");
            }
            plan.Add((entry, variant));
        }
        var items = new JsonArray(plan.Select(item => (JsonNode)new JsonObject
        {
            ["app_id"] = item.Entry.AppId,
            ["game_name"] = item.Entry.GameName,
            ["catalog_status"] = item.Entry.Status,
            ["variant_id"] = item.Variant.VariantId,
        }).ToArray());
        _events.Emit("install", "plan", new JsonObject
        {
            ["count"] = plan.Count,
            ["items"] = items,
        });
        if (dryRun)
        {
            if (preview)
            {
                foreach (var item in plan)
                {
                    var payload = BinaryKeyValues.PreviewJson(
                        await repository.ReadSchemaBytesAsync(item.Variant, args.Has("--offline")));
                    payload["app_id"] = item.Entry.AppId;
                    payload["game_name"] = item.Entry.GameName;
                    payload["variant_id"] = item.Variant.VariantId;
                    payload["action"] = "replace";
                    _events.Emit("install", "item-preview", payload);
                }
            }
            _events.Emit("install", "completed", new JsonObject
            {
                ["succeeded"] = 0,
                ["failed"] = 0,
                ["dry_run"] = true,
                ["exit_code"] = 0,
            });
            return 0;
        }
        RequireYes(args);
        RequireSteamStopped();
        var manager = new TransactionManager(args.DataDirectory);
        var succeeded = 0;
        var failed = 0;
        var firstCode = 0;
        foreach (var item in plan)
        {
            _events.Emit("install", "item-started", new JsonObject
            {
                ["app_id"] = item.Entry.AppId,
                ["game_name"] = item.Entry.GameName,
                ["variant_id"] = item.Variant.VariantId,
            });
            try
            {
                var source = await repository.DownloadSchemaAsync(
                    item.Variant,
                    args.Has("--offline"));
                manager.Install(
                    item.Entry.AppId,
                    SteamLocator.SchemaTarget(steam, item.Entry.AppId),
                    source,
                    item.Variant,
                    "catalog",
                    item.Entry.GameName);
                RefreshDisplayOverride(
                    "install",
                    item.Entry.AppId,
                    item.Entry.GameName,
                    SteamLocator.SchemaTarget(steam, item.Entry.AppId),
                    steam,
                    args.DataDirectory);
                succeeded++;
                _events.Emit("install", "item-succeeded", new JsonObject
                {
                    ["app_id"] = item.Entry.AppId,
                    ["game_name"] = item.Entry.GameName,
                    ["variant_id"] = item.Variant.VariantId,
                });
            }
            catch (SatliException exception)
            {
                failed++;
                firstCode = firstCode == 0 ? exception.ExitCode : firstCode;
                _events.Emit("install", "item-failed", Failure(item.Entry.AppId, exception));
            }
        }
        var code = failed == 0 ? 0 : succeeded > 0 ? 7 : firstCode;
        _events.Emit("install", "completed", new JsonObject
        {
            ["succeeded"] = succeeded,
            ["failed"] = failed,
            ["exit_code"] = code,
        });
        return code;
    }

    private async Task<int> LocalImport(Arguments args)
    {
        var positional = args.Positionals(
            1,
            "--data-dir",
            "--steam-dir",
            "--expected-sha256");
        if (positional.Count != 1)
            throw new UsageException("local-import 需要一个 BIN_OR_ZIP 路径");
        var artifact = LocalImportReader.Read(positional[0]);
        if (args.Value("--expected-sha256") is { } expected
            && !artifact.Sha256.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new IntegrityException("本地导入内容与预览 SHA-256 不一致");
        _events.Emit("local-import", "plan", new JsonObject
        {
            ["count"] = 1,
            ["items"] = new JsonArray(new JsonObject
            {
                ["app_id"] = artifact.AppId,
                ["source"] = artifact.Source,
            }),
        });
        if (args.Has("--dry-run"))
        {
            var payload = (JsonObject)artifact.Preview.DeepClone();
            payload["app_id"] = artifact.AppId;
            payload["source"] = artifact.Source;
            payload["schema_sha256"] = artifact.Sha256;
            payload["variant_id"] = $"local-{artifact.Sha256[..12]}";
            payload["action"] = "replace";
            _events.Emit("local-import", "item-preview", payload);
            _events.Emit("local-import", "completed", new JsonObject
            {
                ["succeeded"] = 0,
                ["failed"] = 0,
                ["dry_run"] = true,
                ["exit_code"] = 0,
            });
            return 0;
        }
        RequireYes(args);
        RequireSteamStopped();
        var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
        var staging = Path.Combine(
            args.DataDirectory,
            "cache",
            "local-import",
            $"{artifact.Sha256}.bin");
        Satli.Core.FileSystem.FileOperations.WriteDurable(staging, artifact.Payload);
        var variant = new SchemaVariant(
            $"local-{artifact.Sha256[..12]}",
            true,
            artifact.SchemaName,
            artifact.Sha256,
            artifact.Payload.Length);
        new TransactionManager(args.DataDirectory).Install(
            artifact.AppId,
            SteamLocator.SchemaTarget(steam, artifact.AppId),
            staging,
            variant,
            "local-import",
            "本地翻译");
        RefreshDisplayOverride(
            "local-import",
            artifact.AppId,
            "本地翻译",
            SteamLocator.SchemaTarget(steam, artifact.AppId),
            steam,
            args.DataDirectory);
        _events.Emit("local-import", "item-succeeded", new JsonObject
        {
            ["app_id"] = artifact.AppId,
            ["schema_sha256"] = artifact.Sha256,
        });
        _events.Emit("local-import", "completed", new JsonObject
        {
            ["succeeded"] = 1,
            ["failed"] = 0,
            ["exit_code"] = 0,
        });
        return 0;
    }

    private static void RequireYes(Arguments args)
    {
        if (!args.Has("--yes"))
            throw new UsageException("非交互模式下必须使用 --yes 确认");
    }

    private static void RequireSteamStopped()
    {
        if (SteamLocator.IsSteamRunning())
            throw new PreflightException("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。");
    }

    private static JsonObject Failure(string appId, SatliException exception) => new()
    {
        ["app_id"] = appId,
        ["message"] = exception.Message,
        ["exit_code"] = exception.ExitCode,
    };
}
