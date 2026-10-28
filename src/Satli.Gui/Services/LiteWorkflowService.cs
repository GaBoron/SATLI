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

/// <summary>Coordinates explicit user choices around the public lite client.</summary>
internal sealed class LiteWorkflowService
{
    private readonly SatliCliService _cli = new();

    public static string DataDirectory(GuiSettings settings) => string.IsNullOrWhiteSpace(settings.DataDirectory)
        ? ApplicationDataPaths.DefaultDataDirectory : settings.DataDirectory;

    public static LiteClient Client(GuiSettings settings) => new(
        SteamLocator.FindSteamDirectory(string.IsNullOrWhiteSpace(settings.SteamDirectory) ? null : settings.SteamDirectory),
        DataDirectory(settings));

    public async Task<(JsonObject Translation, ReplacementPreview Preview)> DownloadAsync(
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
        return (translation, preview);
    }

    public async Task<LiteResult?> ApplyAsync(XamlRoot root, GameItem game, string variantId,
        string language, GuiSettings settings, bool local = false)
    {
        JsonObject translation;
        ReplacementPreview preview;
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
                var payload = BinaryKeyValues.PreviewJson(bytes);
                var achievements = new JsonObject();
                foreach (var row in payload["rows"]!.AsArray())
                {
                    var apiName = row!["api_name"]!.ToString();
                    if (achievements.ContainsKey(apiName)) throw new InvalidDataException("本地 BIN 含重复 API name，无法转换");
                    achievements[apiName] = new JsonObject { ["translations"] = row["translations"]!.DeepClone() };
                }
                var converted = new JsonObject
                {
                    ["version"] = 1, ["app_id"] = game.AppId,
                    ["variant_id"] = System.Text.RegularExpressions.Regex.IsMatch(game.InstalledVariantId, "^[a-z0-9][a-z0-9-]{0,63}$")
                        ? game.InstalledVariantId : "local",
                    ["source_sha256"] = FileOperations.Sha256(bytes),
                    ["languages"] = payload["languages"]!.DeepClone(), ["achievements"] = achievements,
                };
                payload["app_id"] = game.AppId;
                payload["variant_id"] = converted["variant_id"]!.DeepClone();
                using var document = JsonDocument.Parse(payload.ToJsonString());
                return (converted, ReplacementPreview.FromPayload(document.RootElement, game.GameName));
            });
            language = await ChooseLanguageAsync(root, preview.Languages);
            if (language.Length == 0) return null;
        }
        else (translation, preview) = await DownloadAsync(game, variantId, settings);
        if (!await ReplacementConfirmationDialog.ConfirmJsonAsync(root, preview, language)) return null;
        var client = Client(settings);
        if (!await EnsureConnectedAsync(root, client, settings)) return null;
        var previous = await client.ResumeAsync(game.AppId, TimeSpan.Zero);
        if (previous?.Pending == true) return previous;
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
        return await client.ImportAsync(game.AppId, translation, language, local ? "converted" : "catalog_json", conflict);
    }

    public static async Task<bool> EnsureConnectedAsync(XamlRoot root, LiteClient client, GuiSettings settings)
    {
        while (true)
        {
            var connection = await client.InspectAsync();
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"连接状态：{connection.State}。");
            if (connection.Connected) return true;
            var action = connection.State switch
            {
                "not_installed" => "下载并安装 SATLI-lite",
                "millennium_missing" => "打开 Millennium 官方指南",
                "legacy_bridge" => "将旧桥移入回收站",
                _ => "重试连接",
            };
            var dialog = new ContentDialog
            {
                XamlRoot = root, Title = "通过 SATLI-lite 应用 JSON", Content = connection.Message
                    + "\nJSON 缓存会保留；可以关闭提示后继续浏览。",
                PrimaryButtonText = action, CloseButtonText = "暂不应用", DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
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

    public static string Describe(LiteResult result) => result.Pending
        ? $"请求 {result.RequestId} 已投递，等待 lite 处理；可刷新状态。尚未确认导入或启用。"
        : !result.Succeeded ? $"lite 处理失败：{result.ErrorCode}。JSON 缓存已保留；冲突或版本变化需要重新确认。"
        : result.App?["exists"]?.GetValue<bool>() != true ? "已连接 · lite 尚未导入此游戏的译文"
        : $"lite 已保存 · {result.App["variant_id"]} · {result.App["language"]} · "
            + (result.App["effective_enabled"]?.GetValue<bool>() == true ? "显示配置已启用" : "显示未启用（请在 lite 中检查游戏及全局开关）")
            + (result.App["local_edits"]?.GetValue<bool>() == true ? " · 本地编辑或外部导入受更新保护" : "")
            + "。实际页面显示请在 Steam 中确认。";
}
