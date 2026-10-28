using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.SatliLite;
using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.Formats;
using Satli.Core.Imports;
using CorePreviewRow = Satli.Core.Formats.AchievementPreviewRow;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.ViewModels;

public sealed partial class TranslationManagementViewModel
{
    private readonly LiteWorkflowService _lite = new();

    public Task ApplyLiteAsync(XamlRoot root, GameItem game, string variantId = "", string language = "", bool local = false) =>
        RunLiteImportAsync(game, () => _lite.ApplyAsync(root, game, variantId, language, _settings(), local),
            local ? "可信本地快照" : "在线 JSON");

    public Task SendEditedLiteAsync(XamlRoot root, GameItem game, string language, IReadOnlyList<CorePreviewRow> rows) =>
        RunLiteImportAsync(game, async () =>
        {
            // The editor supplies an immutable text snapshot, with no invented BIN hash or draft writeback.
            var translation = await Task.Run(() => LiteTranslation.FromRows(game.AppId, "local-edit", rows));
            var preview = await Task.Run(() => LitePreview(game, translation));
            return await _lite.ApplyTranslationAsync(root, game, translation, preview,
                language, _settings(), "edited");
        }, "编辑器当前译文");

    public async Task ImportLiteFileAsync(XamlRoot root, string sourcePath)
    {
        if (!_operation.TryBegin()) return;
        try
        {
            _operation.SetStatus("正在校验本地 BIN / ZIP 并准备发送…");
            await App.Logs.WriteAsync("信息", "SATLI-lite", "开始校验本地导入文件。");
            var artifact = await Task.Run(() => LocalImportReader.Read(sourcePath, requireAchievements: false));
            var translation = await Task.Run(() => LiteTranslation.FromBin(artifact.Payload, artifact.AppId));
            var game = ManagedGames.FirstOrDefault(item => item.AppId == artifact.AppId)
                ?? Games.FirstOrDefault(item => item.AppId == artifact.AppId)
                ?? new GameItem { AppId = artifact.AppId, GameName = $"Steam 游戏 {artifact.AppId}" };
            var preview = await Task.Run(() => LitePreview(game, translation));
            var result = await _lite.ApplyTranslationAsync(root, game, translation, preview,
                "", _settings(), "converted");
            await CompleteLiteImportAsync(game, result);
        }
        catch (Exception exception) { await ReportLiteExceptionAsync("发送本地译文到 SATLI-lite", exception); }
        finally { _operation.Complete(); }
    }

    private static ReplacementPreview LitePreview(GameItem game, JsonObject translation)
    {
        var payload = TranslationJson.Preview(translation);
        payload["app_id"] = game.AppId;
        payload["variant_id"] = translation["variant_id"]!.DeepClone();
        using var document = JsonDocument.Parse(payload.ToJsonString());
        return ReplacementPreview.FromPayload(document.RootElement, game.GameName);
    }

    private async Task RunLiteImportAsync(GameItem game, Func<Task<LiteResult?>> action, string source)
    {
        if (!_operation.TryBegin()) return;
        try
        {
            _operation.SetStatus("正在准备 SATLI-lite 译文…");
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"开始准备导入。App ID={game.AppId}；来源={source}。");
            await CompleteLiteImportAsync(game, await action());
        }
        catch (Exception exception) { await ReportLiteExceptionAsync("通过 SATLI-lite 应用", exception); }
        finally { _operation.Complete(); }
    }

    private async Task CompleteLiteImportAsync(GameItem game, LiteResult? result)
    {
        if (result is null)
        {
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"用户取消应用。App ID={game.AppId}。");
            return;
        }
        ObserveLite(game, result);
        ShowInfo(game.LiteStateText, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        await App.Logs.WriteAsync(result.State == "failed" ? "错误" : "信息", "SATLI-lite",
            $"请求结果：{result.State}；操作={result.Operation}；App ID={game.AppId}；错误码={result.ErrorCode}。");
        await App.Logs.WriteAsync("详细", "SATLI-lite",
            $"请求 ID={result.RequestId}；处理时间={result.ProcessedAt}。", detailed: true);
    }

    private async Task ReportLiteExceptionAsync(string operation, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"{operation}已取消等待；保留原请求及本地译文。");
            ShowInfo("已取消等待；已投递请求仍可能继续处理，请刷新原请求状态。", InfoBarSeverity.Warning);
        }
        else
        {
            await App.Logs.WriteAsync("错误", "SATLI-lite", $"{operation}失败；本地译文和已有请求记录保留。");
            ShowException(operation, exception);
        }
    }

    public async Task RefreshLiteAsync(GameItem game)
    {
        if (!_operation.TryBegin()) return;
        try
        {
            _operation.SetStatus("正在查询 SATLI-lite 实际状态…");
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"开始刷新请求和游戏状态。App ID={game.AppId}。");
            var client = LiteWorkflowService.Client(_settings());
            var connection = await client.InspectAsync();
            var previous = await client.ResumeAsync(game.AppId, TimeSpan.Zero);
            LiteResult? observed = null;
            if (previous?.Pending == true)
            {
                observed = previous;
                ObserveLite(game, previous);
                if (!connection.Connected) game.LiteStateText += " " + connection.Message;
            }
            else if (connection.Connected)
            {
                observed = await client.QueryAsync(game.AppId);
                ObserveLite(game, observed);
            }
            else
            {
                game.LiteStateText = connection.Message + " 上次结果不能代表当前启用状态。"
                    + (previous is null ? "" : " 上次结果：" + LiteWorkflowService.Describe(previous));
            }
            if (!game.LiteKnown) game.LiteStatusVisible = true;
            ShowInfo(game.LiteStateText, observed?.State == "failed" ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
            await App.Logs.WriteAsync(observed?.State == "failed" ? "错误" : "信息", "SATLI-lite",
                $"状态刷新完成。App ID={game.AppId}；连接={connection.State}；请求={observed?.State}；错误码={observed?.ErrorCode}。");
        }
        catch (Exception exception) { await ReportLiteExceptionAsync("查询 SATLI-lite", exception); }
        finally { _operation.Complete(); }
    }

    private void ObserveLite(GameItem game, LiteResult result, bool historical = false)
    {
        game.LiteStateText = (historical ? "上次处理结果（请刷新）：" : "") + LiteWorkflowService.Describe(result);
        if (result.App?["exists"]?.GetValue<bool>() == true)
            game.LiteStateText += " " + LiteWorkflowService.DescribeUpdate(game, result.App);
        game.LiteKnown = result.Pending || result.Operation == "import" || result.LastImport is not null
            || result.App?["exists"]?.GetValue<bool>() == true;
        var managed = ManagedGames.FirstOrDefault(item => item.AppId == game.AppId);
        if (managed is null && game.LiteKnown) ManagedGames.Add(game);
        else if (managed is not null && !ReferenceEquals(managed, game))
        {
            managed.LiteKnown = game.LiteKnown;
            managed.LiteStateText = game.LiteStateText;
        }
        if (managed is not null && !managed.LiteKnown && managed.InstalledState == "unmanaged")
            ManagedGames.Remove(managed);
    }

    private async Task LoadKnownLiteAsync()
    {
        try
        {
            var client = LiteWorkflowService.Client(_settings());
            try
            {
                var legacyIds = (await client.LegacyAppIdsAsync()).ToHashSet(StringComparer.Ordinal);
                foreach (var game in ManagedGames.Concat(Games).Where(game => legacyIds.Contains(game.AppId)))
                    game.LiteStateText = "发现旧显示桥锁定记录；可主动发送可信译文到 SATLI-lite。旧记录不代表 lite 已导入。";
            }
            catch (Exception exception)
            {
                await App.Logs.WriteAsync("警告", "SATLI-lite", "无法读取旧桥迁移记录；继续加载 lite 请求状态。");
                await App.Logs.WriteExceptionDetailsAsync("SATLI-lite", exception);
            }
            foreach (var appId in client.KnownAppIds().Order(StringComparer.Ordinal))
            {
                try
                {
                    var result = await client.ResumeAsync(appId, TimeSpan.Zero);
                    if (result is null) continue;
                    var game = ManagedGames.FirstOrDefault(item => item.AppId == appId)
                        ?? Games.FirstOrDefault(item => item.AppId == appId)
                        ?? new GameItem { AppId = appId, GameName = $"Steam 游戏 {appId}" };
                    ObserveLite(game, result, historical: !result.Pending);
                }
                catch (Exception exception)
                {
                    await App.Logs.WriteAsync("警告", "SATLI-lite", $"无法读取游戏请求状态，继续加载其他游戏。App ID={appId}。");
                    await App.Logs.WriteExceptionDetailsAsync("SATLI-lite", exception);
                }
            }
        }
        catch (Exception exception)
        {
            await App.Logs.WriteAsync("警告", "SATLI-lite", "无法读取 lite 请求状态；BIN 管理不受影响。");
            await App.Logs.WriteExceptionDetailsAsync("SATLI-lite", exception);
        }
    }
}
