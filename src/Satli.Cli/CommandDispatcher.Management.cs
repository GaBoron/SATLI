using System.Text.Json.Nodes;
using Satli.Core;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Satli.Core.SchemaEditing;
using Satli.Core.State;
using Satli.Core.Steam;
using Satli.Core.SteamDisplay;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private int Restore(Arguments args)
    {
        var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
        var registry = new ManagedGameRegistry(args.DataDirectory, steam);
        var ids = args.Has("--all")
            ? registry.ManagedAppIds().Where(registry.HasActiveTransaction).ToArray()
            : args.Positionals(1, "--data-dir", "--steam-dir").ToArray();
        if (ids.Length == 0)
            throw new UsageException("请指定 APP_ID，或使用 --all");
        var force = args.Has("--force");
        _events.Emit("restore", "plan", new JsonObject
        {
            ["count"] = ids.Length,
            ["items"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject
            {
                ["app_id"] = id,
                ["force"] = force,
            }).ToArray()),
        });
        if (args.Has("--dry-run"))
        {
            if (args.Has("--preview-content"))
            {
                foreach (var id in ids)
                {
                    var source = registry.RestorePreviewSource(
                        id,
                        SteamLocator.SchemaTarget(steam, id));
                    var payload = source is null
                        ? new JsonObject
                        {
                            ["achievement_count"] = 0,
                            ["roundtrip_equal"] = true,
                            ["rows"] = new JsonArray(),
                        }
                        : BinaryKeyValues.PreviewJson(File.ReadAllBytes(source));
                    payload["app_id"] = id;
                    payload["action"] = source is null ? "delete" : "replace";
                    _events.Emit("restore", "item-preview", payload);
                }
            }
            _events.Emit("restore", "completed", new JsonObject
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
        var succeeded = 0;
        var failed = 0;
        var firstCode = 0;
        foreach (var id in ids)
        {
            try
            {
                _events.Emit("restore", "item-started", new JsonObject
                {
                    ["app_id"] = id,
                    ["force"] = force,
                });
                registry.Restore(id, SteamLocator.SchemaTarget(steam, id), force);
                DisableDisplayOverride("restore", id, steam);
                succeeded++;
                _events.Emit("restore", "item-succeeded", new JsonObject
                {
                    ["app_id"] = id,
                    ["force"] = force,
                });
            }
            catch (SatliException exception)
            {
                failed++;
                firstCode = firstCode == 0 ? exception.ExitCode : firstCode;
                _events.Emit("restore", "item-failed", Failure(id, exception));
            }
        }
        var code = failed == 0 ? 0 : succeeded > 0 ? 7 : firstCode;
        _events.Emit("restore", "completed", new JsonObject
        {
            ["succeeded"] = succeeded,
            ["failed"] = failed,
            ["exit_code"] = code,
        });
        return code;
    }

    private int Protect(string[] raw, Arguments args)
    {
        if (raw.Length < 2 || raw[1] is not ("lock" or "unlock"))
            throw new UsageException("protect 需要 lock 或 unlock");
        var enable = raw[1] == "lock";
        if (enable) RequireYes(args);
        var ids = args.Positionals(2, "--data-dir", "--steam-dir").ToArray();
        if (ids.Length == 0) throw new UsageException("请指定 APP_ID");
        var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
        var registry = new ManagedGameRegistry(args.DataDirectory, steam);
        var overrides = new SteamDisplayOverrideStore(
            SteamDisplayPluginInstaller.BridgePath(steam));
        SteamDisplayPluginInstallResult? plugin = null;
        if (enable)
        {
            plugin = SteamDisplayPluginInstaller.EnsureInstalled(
                steam,
                SteamDisplayPluginInstaller.BundledPluginPath());
        }
        _events.Emit("protect", "plan", new JsonObject
        {
            ["action"] = raw[1],
            ["count"] = ids.Length,
        });
        for (var index = 0; index < ids.Length; index++)
        {
            var appId = ids[index];
            var target = SteamLocator.SchemaTarget(steam, appId);
            var managed = registry.Record(appId);
            if (enable)
            {
                if (managed.InstalledState != "installed")
                {
                    throw new PreflightException(
                        $"{appId} 的已安装译文已变化；请先重新安装或保存译文，再启用 Steam 显示覆盖");
                }
                if (!File.Exists(target))
                {
                    throw new PreflightException($"找不到本地成就文件：{target}");
                }
                if (FileOperations.IsReadOnly(target))
                {
                    FileOperations.SetReadOnly(target, false);
                }
                var original = DisplayOverrideBackup("protect", registry, appId, target);
                overrides.Enable(
                    appId,
                    managed.GameName ?? $"Steam 游戏 {appId}",
                    target,
                    original is null ? [] : [original]);
            }
            else
            {
                overrides.Disable(appId);
                if (File.Exists(target) && FileOperations.IsReadOnly(target))
                {
                    FileOperations.SetReadOnly(target, false);
                }
            }
            _events.Emit("protect", "item-succeeded", new JsonObject
            {
                ["app_id"] = appId,
                ["target"] = target,
                ["bridge_path"] = overrides.BridgePath,
                ["plugin_path"] = plugin?.PluginPath ?? "",
                ["plugin_updated"] = plugin?.Updated ?? false,
                ["plugin_runtime_active"] = plugin?.RuntimeActive ?? false,
                ["display_override_enabled"] = enable,
                ["legacy_read_only_cleared"] = File.Exists(target)
                    && !FileOperations.IsReadOnly(target),
                ["action"] = enable ? "display-override-enabled" : "display-override-disabled",
                ["position"] = index + 1,
            });
        }
        _events.Emit("protect", "completed", new JsonObject
        {
            ["count"] = ids.Length,
            ["exit_code"] = 0,
        });
        return 0;
    }

    private int Petition(Arguments args)
    {
        var positions = args.Positionals(2, "--steam-dir", "--output");
        if (positions.Count != 1)
            throw new UsageException("petition export 需要 APP_ID");
        var appId = positions[0];
        var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
        var source = SteamLocator.SchemaTarget(steam, appId);
        if (!File.Exists(source))
            throw new PreflightException($"找不到本地成就文件：{source}");
        var output = Path.GetFullPath(args.Required("--output"));
        if (File.Exists(output) && !args.Has("--overwrite"))
            throw new UsageException($"目标已存在：{output}");
        var payload = File.ReadAllBytes(source);
        BinaryKeyValues.Preview(payload);
        SchemaEditor.WriteZip(output, Path.GetFileName(source), payload);
        _events.Emit("petition-export", "completed", new JsonObject
        {
            ["app_id"] = appId,
            ["source"] = source,
            ["output"] = output,
            ["file_size_bytes"] = payload.Length,
            ["exit_code"] = 0,
        });
        return 0;
    }
}
