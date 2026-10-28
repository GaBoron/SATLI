using System.Text.Json.Nodes;
using Satli.Core;
using Satli.Core.Formats;
using Satli.Core.Imports;
using Satli.Core.SatliLite;
using Satli.Core.State;
using Satli.Core.Steam;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private async Task<int> Lite(string[] raw, Arguments args)
    {
        if (raw.Length < 2) throw new UsageException("lite 缺少 inspect/status/resume/import 子命令");
        var operation = $"lite-{raw[1]}";
        var ids = args.Positionals(2, "--data-dir", "--steam-dir", "--source", "--variant", "--language", "--replace-revision");
        var steam = SteamLocator.FindSteamDirectory(args.SteamDirectory);
        var client = new LiteClient(steam, args.DataDirectory);
        if (raw[1] == "inspect")
        {
            if (ids.Count != 0) throw new UsageException("lite inspect 不接受 APP_ID");
            var connection = await client.InspectAsync();
            var payload = new JsonObject
            {
                ["state"] = connection.State, ["message"] = connection.Message,
                ["connected"] = connection.Connected, ["can_submit"] = connection.CanSubmit,
                ["plugin_version"] = connection.PluginVersion,
                ["translation_versions"] = new JsonArray(connection.TranslationVersions.Select(version => (JsonNode)JsonValue.Create(version)!).ToArray()),
                ["exit_code"] = 0,
            };
            _events.Emit(operation, "completed", payload);
            if (!_events.JsonLines) await _output.WriteLineAsync(payload.ToJsonString());
            return 0;
        }
        if (ids.Count != 1 || raw[1] is not ("status" or "resume" or "import"))
            throw new UsageException("lite status/resume/import 需要一个 APP_ID");
        var appId = ids[0];
        _events.Emit(operation, "item-started", new JsonObject { ["app_id"] = appId });
        LiteResult? result;
        if (raw[1] == "status") result = await client.QueryAsync(appId);
        else if (raw[1] == "resume") result = await client.ResumeAsync(appId, TimeSpan.Zero);
        else
        {
            if (!args.Has("--dry-run")) RequireYes(args);
            var selectedSources = (args.Has("--catalog") ? 1 : 0) + (args.Has("--source") ? 1 : 0) + (args.Has("--managed") ? 1 : 0);
            if (selectedSources != 1) throw new UsageException("请选择唯一来源：--catalog、--source FILE 或 --managed");
            var variantId = args.Value("--variant") ?? (args.Has("--catalog") ? "default" : "local");
            var sourceKind = "converted";
            string? sourceVersion = null;
            JsonObject translation;
            if (args.Has("--catalog"))
            {
                var repository = Repository(args);
                var catalog = await repository.LoadAsync(args.Has("--offline"), refresh: !args.Has("--catalog-cache-only"));
                var variant = catalog.Entries.GetValueOrDefault(appId)?.Variants.FirstOrDefault(item => item.VariantId == variantId)
                    ?? throw new UsageException("翻译库中没有所选 App ID 或 Variant");
                var path = await repository.DownloadJsonAsync(appId, variant, args.Has("--offline"));
                var bytes = await File.ReadAllBytesAsync(path);
                translation = TranslationJson.Parse(bytes, appId, variant);
                sourceKind = "catalog_json";
                sourceVersion = $"catalog-json:{Satli.Core.FileSystem.FileOperations.Sha256(bytes)}";
            }
            else if (args.Has("--managed"))
            {
                var source = new ManagedGameRegistry(args.DataDirectory, steam).DisplayOverrideSource(appId, SteamLocator.SchemaTarget(steam, appId));
                translation = LiteTranslation.FromBin(await File.ReadAllBytesAsync(source), appId, variantId);
            }
            else
            {
                var source = args.Required("--source");
                if (Path.GetExtension(source).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    if (new FileInfo(source).Length > TranslationJson.MaximumBytes) throw new IntegrityException("JSON 超过体积上限");
                    translation = TranslationJson.Parse(await File.ReadAllBytesAsync(source), appId);
                    sourceKind = "edited";
                }
                else
                {
                    var artifact = LocalImportReader.Read(source, requireAchievements: false);
                    if (artifact.AppId != appId) throw new IntegrityException("本地文件的 App ID 与目标不一致");
                    translation = LiteTranslation.FromBin(artifact.Payload, appId, variantId);
                }
            }
            var language = args.Required("--language");
            if (!translation["languages"]!.AsArray().Any(value => value?.ToString() == language))
                throw new UsageException("译文不包含所选目标语言");
            if (args.Has("--dry-run"))
            {
                var preview = args.Has("--preview-content") ? TranslationJson.Preview(translation) : new JsonObject();
                preview["app_id"] = appId;
                preview["validated"] = true;
                preview["exit_code"] = 0;
                _events.Emit(operation, "completed", preview);
                if (!_events.JsonLines) await _output.WriteLineAsync(preview.ToJsonString());
                return 0;
            }
            var revision = args.Value("--replace-revision");
            if (args.Has("--allow-local-edits") && revision is null)
                throw new UsageException("覆盖本地编辑还需提供刚查询到的 --replace-revision");
            JsonObject? conflict = revision is null ? null : new JsonObject
            {
                ["mode"] = "replace", ["expected_revision"] = revision,
                ["allow_local_edits"] = args.Has("--allow-local-edits"),
            };
            result = await client.ImportAsync(appId, translation, language, sourceKind, conflict, sourceVersion: sourceVersion);
        }
        var exitCode = result?.State == "failed" ? 3 : 0;
        var status = result is null ? new JsonObject { ["state"] = "not_tracked" } : LitePayload(result);
        status["app_id"] = appId;
        status["connection"] = (await client.InspectAsync()).State;
        status["exit_code"] = exitCode;
        _events.Emit(operation, "completed", status);
        if (!_events.JsonLines) await _output.WriteLineAsync(status.ToJsonString());
        return exitCode;
    }

    private static JsonObject LitePayload(LiteResult result) => new()
    {
        ["request_id"] = result.RequestId, ["operation"] = result.Operation, ["state"] = result.State,
        ["pending"] = result.Pending, ["succeeded"] = result.Succeeded,
        ["app"] = result.App?.DeepClone(), ["error_code"] = result.ErrorCode,
        ["processed_at"] = result.ProcessedAt,
        ["last_import"] = result.LastImport is null ? null : LitePayload(result.LastImport),
    };

    private async Task<int> InstallLitePlugin(Arguments args)
    {
        RequireYes(args);
        if (args.Has("--offline")) throw new PreflightException("离线模式下无法下载 SATLI-lite");
        var steam = Satli.Core.Steam.SteamLocator.FindSteamDirectory(args.SteamDirectory);
        using var http = NetworkClient();
        _events.Emit("lite-install", "item-started", new JsonObject { ["message"] = "正在下载官方 SATLI-lite 安装包" });
        await Satli.Core.SatliLite.LitePluginInstaller.InstallAsync(steam, http);
        _events.Emit("lite-install", "completed", new JsonObject
        {
            ["message"] = "插件文件已校验并保存；请在 Steam → Millennium → Plugins 启用并重启 Steam。尚未应用译文。",
            ["exit_code"] = 0,
        });
        return 0;
    }

}
