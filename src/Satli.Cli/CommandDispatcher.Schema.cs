using System.Text.Json.Nodes;
using Satli.Core;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Satli.Core.SchemaEditing;
using Satli.Core.Steam;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private int Schema(string[] raw, Arguments args)
    {
        if (raw.Length < 2)
            throw new UsageException("schema 缺少子命令");
        if (raw[1] == "revisions")
            return SchemaRevisions(raw, args);
        var positions = args.Positionals(
            2,
            "--data-dir",
            "--steam-dir",
            "--target-language",
            "--edits-file",
            "--format",
            "--output",
            "--game-name",
            "--variant-id");
        if (positions.Count != 1)
            throw new UsageException($"schema {raw[1]} 需要 APP_ID");
        var appId = positions[0];
        var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
        var source = SteamLocator.SchemaTarget(steam, appId);
        JsonObject report;
        switch (raw[1])
        {
            case "inspect":
                report = _editor.Inspect(source, appId, args.DataDirectory);
                break;
            case "restore":
                RequireYes(args);
                RequireSteamStopped();
                report = _editor.Restore(
                    source,
                    appId,
                    args.DataDirectory,
                    args.Has("--force"));
                CaptureRevision(
                    report,
                    args,
                    appId,
                    File.ReadAllBytes(source),
                    "restore");
                break;
            case "export":
            {
                var rendered = Render(args, source, appId);
                report = _editor.Export(
                    rendered,
                    source,
                    appId,
                    args.Required("--output"),
                    args.Required("--format"));
                CaptureRevision(report, args, appId, rendered.Payload, "export");
                break;
            }
            case "draft":
            {
                var rendered = Render(args, source, appId);
                report = rendered.Report;
                CaptureRevision(report, args, appId, rendered.Payload, "draft");
                break;
            }
            case "apply":
            {
                RequireYes(args);
                RequireSteamStopped();
                var rendered = Render(args, source, appId);
                report = _editor.Apply(
                    source,
                    appId,
                    rendered.Payload,
                    args.DataDirectory,
                    args.Value("--game-name"),
                    args.Required("--target-language"),
                    rendered.Report);
                CaptureRevision(
                    report,
                    args,
                    appId,
                    File.ReadAllBytes(source),
                    "apply");
                break;
            }
            default:
                throw new UsageException($"未知 schema 子命令：{raw[1]}");
        }
        var operation = $"schema-{raw[1]}";
        _events.Emit(operation, "item-succeeded", report);
        _events.Emit(operation, "completed", new JsonObject
        {
            ["count"] = 1,
            ["exit_code"] = 0,
        });
        return 0;
    }

    private int SchemaRevisions(string[] raw, Arguments args)
    {
        if (raw.Length < 3)
            throw new UsageException("schema revisions 缺少子命令");
        var command = raw[2];
        var positions = args.Positionals(
            3,
            "--data-dir",
            "--steam-dir",
            "--format",
            "--output");
        var appId = positions.Count > 0 ? positions[0] : null;
        var store = new SchemaRevisionStore(args.DataDirectory);
        var operation = $"schema-revisions-{command}";
        if (command == "verify")
        {
            var report = store.Verify(appId);
            _events.Emit(operation, "item-succeeded", report);
            _events.Emit(operation, "completed", new JsonObject
            {
                ["count"] = 1,
                ["exit_code"] = 0,
            });
            return 0;
        }
        if (appId is null)
            throw new UsageException("缺少 APP_ID");
        if (command == "list")
        {
            var current = CurrentHash(args, appId);
            var revisions = store.List(appId);
            _events.Emit(operation, "plan", new JsonObject
            {
                ["count"] = revisions.Count,
            });
            foreach (var item in revisions)
            {
                var payload = item.ToJson();
                payload["is_current"] = item.SchemaSha256 == current;
                _events.Emit(operation, "item-succeeded", payload);
            }
            _events.Emit(operation, "completed", new JsonObject
            {
                ["count"] = revisions.Count,
                ["exit_code"] = 0,
            });
            return 0;
        }
        if (positions.Count < 2)
            throw new UsageException("缺少 REVISION");
        var revisionId = positions[1];
        var revision = store.Get(appId, revisionId);
        JsonObject result;
        if (command == "show")
        {
            result = revision.ToJson(true);
            result["is_current"] = revision.SchemaSha256 == CurrentHash(args, appId);
            try
            {
                var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
                var current = SteamLocator.SchemaTarget(steam, appId);
                if (File.Exists(current))
                    result["current_preview"] = BinaryKeyValues.PreviewJson(
                        File.ReadAllBytes(current));
            }
            catch (SatliException)
            {
            }
        }
        else if (command == "export")
        {
            revision = store.Export(
                appId,
                revisionId,
                args.Required("--output"),
                args.Required("--format"));
            result = revision.ToJson();
            result["output"] = Path.GetFullPath(args.Required("--output"));
        }
        else if (command == "activate")
        {
            RequireYes(args);
            RequireSteamStopped();
            var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
            result = _editor.Apply(
                SteamLocator.SchemaTarget(steam, appId),
                appId,
                revision.Schema,
                args.DataDirectory,
                revision.GameName,
                revision.TargetLanguage);
            var committed = store.Record(
                appId,
                revision.Schema,
                "activate",
                revision.GameName,
                revision.TargetLanguage,
                revision.AchievementCount,
                variantId: revision.VariantId);
            Merge(result, committed.ToJson());
        }
        else
        {
            throw new UsageException($"未知 revisions 子命令：{command}");
        }
        _events.Emit(operation, "item-succeeded", result);
        _events.Emit(operation, "completed", new JsonObject
        {
            ["count"] = 1,
            ["exit_code"] = 0,
        });
        return 0;
    }

    private RenderedSchema Render(Arguments args, string source, string appId) =>
        _editor.Render(
            source,
            appId,
            args.Required("--target-language"),
            args.Required("--edits-file"),
            args.Has("--allow-incomplete"));

    private void CaptureRevision(
        JsonObject report,
        Arguments args,
        string appId,
        byte[] payload,
        string action)
    {
        try
        {
            var revision = new SchemaRevisionStore(args.DataDirectory).Record(
                appId,
                payload,
                action,
                args.Value("--game-name") ?? "",
                args.Value("--target-language") ?? "",
                report["achievement_count"]?.GetValue<int>() ?? 0,
                report["changed_names"]?.GetValue<int>() ?? 0,
                report["changed_descriptions"]?.GetValue<int>() ?? 0,
                args.Value("--variant-id") ?? "");
            report["revision_commit"] = revision.Commit;
        }
        catch (Exception exception)
        {
            report["revision_warning"] =
                $"操作已完成，但无法写入修订历史：{exception.Message}";
        }
    }

    private static string CurrentHash(Arguments args, string appId)
    {
        try
        {
            var path = SteamLocator.SchemaTarget(
                SteamLocator.FindSteamDirectory(args.SteamDirectory),
                appId);
            return File.Exists(path) ? FileOperations.Sha256(path) : "";
        }
        catch (SatliException)
        {
            return "";
        }
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
            target[pair.Key] = pair.Value?.DeepClone();
    }
}
