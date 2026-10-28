using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Satli.Core.SatliLite;
using Satli.Core.State;
using Satli.Core.Steam;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

internal sealed record DownloadedJson(JsonObject Translation, ReplacementPreview Preview, string ContentHash);

/// <summary>Coordinates explicit user choices around the public lite client.</summary>
internal sealed class LiteWorkflowService
{
    private readonly SatliCliService _cli = new();

    public static string DataDirectory(GuiSettings settings) => string.IsNullOrWhiteSpace(settings.DataDirectory)
        ? ApplicationDataPaths.DefaultDataDirectory : settings.DataDirectory;

    public static LiteClient Client(GuiSettings settings) => new(
        SteamLocator.FindSteamDirectory(string.IsNullOrWhiteSpace(settings.SteamDirectory) ? null : settings.SteamDirectory),
        DataDirectory(settings));

    public async Task<DownloadedJson> DownloadAsync(
        GameItem game, string variantId, GuiSettings settings)
    {
        await App.Logs.WriteAsync("信息", "JSON 下载", $"开始读取在线 JSON。App ID={game.AppId}；Variant={variantId}。");
        var arguments = new TranslationCliArguments(() => settings).CatalogJson(game, variantId);
        var result = await Task.Run(() => _cli.RunAsync(arguments,
            networkSettings: settings.Network, downloadSourceSettings: settings.DownloadSources));
        if (!result.IsSuccess) throw new InvalidOperationException(result.ErrorMessage);
        var preview = await Task.Run(() => TranslationPreviewParser.ParseBatch(result, [game]).Single());
        var completed = result.Events.Single(item => item.Event == "completed");
        var path = completed.Payload.GetProperty("path").GetString()!;
        var expectedHash = completed.Payload.GetProperty("json_sha256").GetString();
        var translation = await Task.Run(async () =>
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length > TranslationJson.MaximumBytes || FileOperations.Sha256(bytes) != expectedHash)
                throw new InvalidDataException("JSON 缓存自校验后发生变化，请重新下载。");
            return JsonNode.Parse(bytes)!.AsObject();
        });
        await App.Logs.WriteAsync("信息", "JSON 下载", $"JSON 已下载并校验。App ID={game.AppId}；成就={preview.AchievementCount}；语言={preview.Languages.Count}。");
        return new(translation, preview, expectedHash!);
    }

    public async Task<LiteResult?> ApplyAsync(XamlRoot root, GameItem game, string variantId,
        string language, GuiSettings settings, bool local = false)
    {
        JsonObject translation;
        ReplacementPreview preview;
        string? sourceVersion = null;
        if (local)
        {
            var steam = SteamLocator.FindSteamDirectory(string.IsNullOrWhiteSpace(settings.SteamDirectory) ? null : settings.SteamDirectory);
            var data = DataDirectory(settings);
            (translation, preview) = await Task.Run(() =>
            {
                // Only the hash-verified installed snapshot is eligible after Steam overwrites a BIN.
                var source = new ManagedGameRegistry(data, steam).DisplayOverrideSource(game.AppId,
                    SteamLocator.SchemaTarget(steam, game.AppId));
                var bytes = File.ReadAllBytes(source);
                var converted = LiteTranslation.FromBin(bytes, game.AppId, game.InstalledVariantId);
                var payload = TranslationJson.Preview(converted);
                payload["app_id"] = game.AppId;
                payload["variant_id"] = converted["variant_id"]!.DeepClone();
                using var document = JsonDocument.Parse(payload.ToJsonString());
                return (converted, ReplacementPreview.FromPayload(document.RootElement, game.GameName));
            });
            language = await ChooseLanguageAsync(root, preview.Languages);
            if (language.Length == 0) return null;
        }
        else
        {
            var downloaded = await DownloadAsync(game, variantId, settings);
            translation = downloaded.Translation;
            preview = downloaded.Preview;
            sourceVersion = $"catalog-json:{downloaded.ContentHash}";
        }
        return await ApplyTranslationAsync(root, game, translation, preview, language, settings,
            local ? "converted" : "catalog_json", sourceVersion);
    }

    public async Task<LiteResult?> ApplyTranslationAsync(XamlRoot root, GameItem game, JsonObject translation,
        ReplacementPreview preview, string language, GuiSettings settings, string sourceKind, string? sourceVersion = null)
    {
        if (language.Length == 0) language = await ChooseLanguageAsync(root, preview.Languages);
        if (language.Length == 0) return null;
        if (!preview.Languages.Contains(language)) throw new InvalidDataException("译文不包含所选目标语言。");
        if (!await ReplacementConfirmationDialog.ConfirmJsonAsync(root, preview, language)) return null;
        var client = Client(settings);
        if (!await EnsureConnectedAsync(root, client, settings, game.AppId)) return null;
        var previous = await client.ResumeAsync(game.AppId, TimeSpan.Zero);
        if (previous?.Pending == true) return previous;
        var connection = await client.InspectAsync();
        // The plugin enforces reject at execution time, so an offline request cannot silently replace existing text.
        if (!connection.Connected)
            return await Task.Run(() => client.ImportAsync(game.AppId, translation, language, sourceKind, sourceVersion: sourceVersion));
        var query = await client.QueryAsync(game.AppId);
        if (!query.Succeeded) return query;
        JsonObject? conflict = null;
        if (query.App?["exists"]?.GetValue<bool>() == true)
        {
            var localEdits = query.App["local_edits"]?.GetValue<bool>() == true;
            var dialog = new ContentDialog
            {
                XamlRoot = root, Title = "确认覆盖 SATLI-lite 中的译本",
                Content = localEdits
                    ? "lite 已有本地编辑或外部导入的译本。继续将覆盖当前内容，并由 lite 保留历史。"
                    : "lite 已保存此游戏的译本。继续将替换该译本，并由 lite 保留历史。",
                PrimaryButtonText = "覆盖并请求启用", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            conflict = new JsonObject
            {
                ["mode"] = "replace", ["expected_revision"] = query.App["revision"]!.DeepClone(),
                ["allow_local_edits"] = localEdits,
            };
        }
        return await Task.Run(() => client.ImportAsync(game.AppId, translation, language, sourceKind, conflict,
            sourceVersion: sourceVersion));
    }

    public static async Task<bool> EnsureConnectedAsync(XamlRoot root, LiteClient client, GuiSettings settings, string appId)
    {
        while (true)
        {
            var connection = await client.InspectAsync();
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"连接状态：{connection.State}。");
            if (connection.Connected) return true;
            var migration = "";
            if (connection.State == "legacy_bridge")
            {
                var ids = await client.LegacyAppIdsAsync();
                migration = ids.Contains(appId)
                    ? "\n发现此游戏的旧显示锁定记录。将使用刚才预览的译文通过新协议迁移；旧桥快照不会直接导入。"
                    : $"\n旧桥记录了 {ids.Count} 个游戏。请逐个选择对应的已管理译文迁移。";
                await App.Logs.WriteAsync("信息", "SATLI-lite", $"检测到旧桥迁移记录。游戏数={ids.Count}；当前 App ID={appId}。");
            }
            var action = connection.State switch
            {
                "not_installed" => "安装 lite",
                "millennium_missing" => "打开 Millennium 官方指南",
                "legacy_bridge" => "将旧桥移入回收站",
                _ => connection.CanSubmit ? "排队发送" : "重试连接",
            };
            var dialog = new ContentDialog
            {
                XamlRoot = root, Title = "使用 lite", Content = connection.Message + migration
                    + (connection.CanSubmit ? "\n排队请求默认拒绝覆盖已有译本；发生冲突时需连接后重新查询和确认。" : "")
                    + "\n本地译文和 JSON 缓存会保留；可以关闭提示后继续浏览。",
                PrimaryButtonText = action, CloseButtonText = "暂不应用", DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
            if (connection.CanSubmit) return true;
            if (connection.State == "millennium_missing")
            {
                await new ExternalUriLauncher().LaunchAsync(new Uri("https://docs.steambrew.app/users/getting-started/installation"));
                return false;
            }
            if (connection.State == "legacy_bridge")
            {
                if (new SteamProcessController().IsRunning())
                    throw new InvalidOperationException("请先正常退出 Steam，再将旧桥移入回收站。原译文快照和备份会保留。");
                var legacy = Path.Combine(Path.GetDirectoryName(client.PluginPath)!, "satli.star");
                await Task.Run(() => RecycleBin.FileIfExists(legacy));
                await App.Logs.WriteAsync("信息", "SATLI-lite", "旧显示桥安装包已移入回收站，保留译文和备份。");
            }
            if (connection.State == "not_installed")
            {
                if (settings.Offline) throw new InvalidOperationException("离线模式下无法下载 SATLI-lite；JSON 缓存已保留。");
                await App.Logs.WriteAsync("信息", "SATLI-lite", "开始下载官方插件，必要时请求管理员权限。");
                var installed = await new SatliCliService().RunAsync(
                    ["lite-install", "--steam-dir", client.SteamDirectory, "--yes", "--jsonl"],
                    networkSettings: settings.Network, downloadSourceSettings: settings.DownloadSources);
                if (!installed.IsSuccess) throw new InvalidOperationException(installed.ErrorMessage);
                await App.Logs.WriteAsync("信息", "SATLI-lite", "SATLI-lite 安装包已校验并保存；等待在 Steam 中启用和重启。");
            }
        }
    }

    private static async Task<string> ChooseLanguageAsync(XamlRoot root, IReadOnlyList<string> languages)
    {
        var box = new ComboBox { ItemsSource = languages, SelectedItem = languages.Contains("schinese") ? "schinese" : languages.FirstOrDefault() };
        var dialog = new ContentDialog { XamlRoot = root, Title = "选择发送到 SATLI-lite 的语言", Content = box,
            PrimaryButtonText = "继续", CloseButtonText = "取消" };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.SelectedItem?.ToString() ?? "" : "";
    }

    public static string Describe(LiteResult result) => DescribeCurrent(result)
        + (result.ProcessedAt is { } time ? $" 处理时间：{DateTimeOffset.FromUnixTimeSeconds(time).ToLocalTime():g}。" : "")
        + (result.LastImport is { } last ? $" 最近发送：{DescribeCurrent(last)}" : "");

    private static string DescribeCurrent(LiteResult result) => result.Pending
        ? $"请求 {result.RequestId} " + (result.State switch
        {
            "prepared" => "已保存待发送；请重试投递。",
            "accepted" => "已被 lite 接收，等待最终处理结果。",
            _ => "已投递，等待 Steam / lite 启动或处理。",
        }) + "可刷新原请求；尚未确认导入或启用。"
        : !result.Succeeded ? $"lite 处理失败：{result.ErrorCode}。{DescribeError(result.ErrorCode)}本地译文和 JSON 缓存已保留。"
        : result.App?["exists"]?.GetValue<bool>() != true ? "已连接 · lite 尚未导入此游戏的译文"
        : $"lite 已保存 · {result.App["variant_id"]} · {result.App["language"]} · "
            + (result.App["effective_enabled"]?.GetValue<bool>() == true ? "显示配置已启用" : "显示未启用（请在 lite 中检查游戏及全局开关）")
            + (result.App["local_edits"]?.GetValue<bool>() == true ? " · 本地编辑或外部导入受更新保护" : "")
            + "。实际页面显示请在 Steam 中确认。";

    private static string DescribeError(string code) => code switch
    {
        "CONFLICT" or "LOCAL_EDITS_CONFLICT" or "STALE_REVISION" => "请连接 lite 后重新发送，查询当前译本并再次确认覆盖。",
        "UNSUPPORTED_PROTOCOL" => "请升级 SATLI 或 lite。",
        "INVALID_TRANSLATION" or "APP_ID_MISMATCH" or "LANGUAGE_UNAVAILABLE" => "请检查译文格式和目标语言后重试。",
        "STORAGE_ERROR" or "APPLY_FAILED" => "请检查插件存储权限及容量，刷新原请求后再决定是否重试。",
        "REQUEST_ID_REUSED" => "请求标识已被其他内容占用，请重新发送并生成新请求。",
        _ => "请刷新请求状态并检查 lite 日志。",
    };

    public static string DescribeUpdate(GameItem game, JsonObject app)
    {
        var variant = game.Variants.FirstOrDefault(item => item.VariantId == app["variant_id"]?.ToString());
        if (variant is null) return game.Variants.Count == 0 ? "目录信息未加载，更新状态待核对。" : "目录中没有此译本，更新状态需核对。";
        string? identity;
        string latest;
        if (variant.JsonVersion == 2)
        {
            latest = variant.JsonSha256;
            var origin = app["origin"] as JsonObject;
            var version = origin?["version"]?.ToString() ?? "";
            identity = origin?["kind"]?.ToString() == "community" ? app["json_sha256"]?.ToString()
                : origin?["kind"]?.ToString() == "external" && origin["client_id"]?.ToString() == LiteClient.ClientId
                    && origin["source_kind"]?.ToString() == "catalog_json" && version.StartsWith("catalog-json:", StringComparison.Ordinal)
                    ? version["catalog-json:".Length..] : null;
        }
        else
        {
            latest = variant.Sha256;
            identity = app["source_sha256"]?.ToString();
        }
        if (app["origin"]?["kind"]?.ToString() == "local_edit" || string.IsNullOrEmpty(identity) || latest.Length == 0)
            return "本地或未知来源，更新状态需在 lite 中核对。";
        return identity == latest ? "与当前目录版本一致。" : "社区目录已有不同版本；应用前仍需确认覆盖。";
    }
}
