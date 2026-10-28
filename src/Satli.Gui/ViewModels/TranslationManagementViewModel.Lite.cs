using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.SatliLite;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.ViewModels;

public sealed partial class TranslationManagementViewModel
{
    private readonly LiteWorkflowService _lite = new();

    public async Task DownloadJsonAsync(XamlRoot root, GameItem game, string variantId)
    {
        if (!_operation.TryBegin()) return;
        try
        {
            _operation.SetStatus("正在下载和校验显示文本 JSON…");
            var (_, preview) = await _lite.DownloadAsync(game, variantId, _settings());
            await ReplacementConfirmationDialog.ShowCatalogReadOnlyAsync(root, [preview], "JSON 已下载到缓存 · 成就预览");
            ShowInfo("JSON 已下载并校验，保存在 SATLI 缓存中；尚未导入 lite。", InfoBarSeverity.Success);
        }
        catch (Exception exception) { ShowException("下载 JSON", exception); }
        finally { _operation.Complete(); }
    }

    public async Task ApplyLiteAsync(XamlRoot root, GameItem game, string variantId = "", string language = "", bool local = false)
    {
        if (!_operation.TryBegin()) return;
        try
        {
            _operation.SetStatus("正在准备 SATLI-lite 译文…");
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"开始准备导入。App ID={game.AppId}；来源={(local ? "可信本地快照" : "在线 JSON")}。");
            var result = await _lite.ApplyAsync(root, game, variantId, language, _settings(), local);
            if (result is null)
            {
                await App.Logs.WriteAsync("信息", "SATLI-lite", $"用户取消应用。App ID={game.AppId}。");
                return;
            }
            ObserveLite(game, result);
            ShowInfo(game.LiteStateText, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            await App.Logs.WriteAsync(result.State == "failed" ? "错误" : "信息", "SATLI-lite",
                $"请求结果：{result.State}；操作={result.Operation}；App ID={game.AppId}；错误码={result.ErrorCode}。");
        }
        catch (Exception exception) { ShowException("通过 SATLI-lite 应用", exception); }
        finally { _operation.Complete(); }
    }

    public async Task RefreshLiteAsync(GameItem game)
    {
        if (!_operation.TryBegin()) return;
        try
        {
            _operation.SetStatus("正在查询 SATLI-lite 实际状态…");
            var client = LiteWorkflowService.Client(_settings());
            var connection = await client.InspectAsync();
            var previous = await client.ResumeAsync(game.AppId, TimeSpan.Zero);
            if (previous?.Pending == true)
                ObserveLite(game, previous);
            else if (connection.Connected)
                ObserveLite(game, await client.QueryAsync(game.AppId));
            else game.LiteStateText = connection.Message + " 上次结果不能代表当前启用状态。";
            ShowInfo(game.LiteStateText, InfoBarSeverity.Informational);
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"状态刷新完成。App ID={game.AppId}；连接={connection.State}。");
        }
        catch (Exception exception) { ShowException("查询 SATLI-lite", exception); }
        finally { _operation.Complete(); }
    }

    private void ObserveLite(GameItem game, LiteResult result, bool historical = false)
    {
        game.LiteStateText = (historical ? "上次处理结果（请刷新）：" : "") + LiteWorkflowService.Describe(result);
        game.LiteKnown = result.Pending || result.App?["exists"]?.GetValue<bool>() == true;
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
            foreach (var appId in client.KnownAppIds().Order(StringComparer.Ordinal))
            {
                var result = await client.ResumeAsync(appId, TimeSpan.Zero);
                if (result is null) continue;
                var game = ManagedGames.FirstOrDefault(item => item.AppId == appId)
                    ?? Games.FirstOrDefault(item => item.AppId == appId)
                    ?? new GameItem { AppId = appId, GameName = $"Steam 游戏 {appId}" };
                ObserveLite(game, result, historical: !result.Pending);
            }
        }
        catch (Exception exception)
        {
            await App.Logs.WriteAsync("警告", "SATLI-lite", "无法读取 lite 请求状态；BIN 管理不受影响。");
            await App.Logs.WriteExceptionDetailsAsync("SATLI-lite", exception);
        }
    }
}
