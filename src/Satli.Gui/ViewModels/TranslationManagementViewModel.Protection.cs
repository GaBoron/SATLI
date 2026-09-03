using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.ViewModels;

public sealed partial class TranslationManagementViewModel
{
    private readonly SteamSchemaChangeMonitor _schemaMonitor = new();
    private readonly SemaphoreSlim _schemaChangeRefresh = new(1, 1);

    public IDisposable BeginSchemaMonitoringSuppression(IEnumerable<string> appIds) =>
        _schemaMonitor.BeginSuppression(appIds);

    public async Task SetProtectionAsync(IReadOnlyList<GameItem> selected, bool enable)
    {
        if (selected.Count == 0 || !_operation.TryBegin())
        {
            return;
        }
        var appIds = selected.Select(item => item.AppId).ToArray();
        using var monitoringSuppression = _schemaMonitor.BeginSuppression(appIds);
        try
        {
            var result = await RunCliAsync(
                _arguments.Protect(selected, enable),
                enable ? "正在生成 Steam 成就显示覆盖…" : "正在解除 Steam 成就显示覆盖…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await LoadManagedCoreAsync(forceOffline: true);
            var pluginActive = result.Events
                .Where(item => item.Event == "item-succeeded")
                .Any(item => item.Payload.TryGetProperty(
                        "plugin_runtime_active",
                        out var active)
                    && active.ValueKind == System.Text.Json.JsonValueKind.True);
            var pluginUpdated = result.Events
                .Where(item => item.Event == "item-succeeded")
                .Any(item => item.Payload.TryGetProperty(
                        "plugin_updated",
                        out var updated)
                    && updated.ValueKind == System.Text.Json.JsonValueKind.True);
            var snapshotReused = result.Events
                .Where(item => item.Event == "item-succeeded")
                .Any(item => item.Payload.TryGetProperty(
                        "trusted_snapshot_reused",
                        out var reused)
                    && reused.ValueKind == System.Text.Json.JsonValueKind.True);
            var snapshotMessage = snapshotReused
                ? "已直接复用 SATLI 最后一次校验通过的译文快照，无需重新下载或改写 Steam 文件。"
                : string.Empty;
            ShowInfo(
                enable
                    ? pluginUpdated
                        ? $"已写入 {selected.Count} 个游戏的显示锁定，并安装或更新 SATLI 的 Millennium 插件。{snapshotMessage}请重启 Steam；首次使用时还需在 Millennium → Plugins 中启用 SATLI Achievement Display Bridge。SATLI 无需后台运行。"
                        : pluginActive
                        ? $"已锁定 {selected.Count} 个游戏的 Steam 成就显示；{snapshotMessage}运行中的插件通常会在约 2 秒内自动刷新，无需重新启用或重启 Steam。SATLI 现在可以退出。"
                        : $"已写入 {selected.Count} 个游戏的显示锁定和 Millennium 插件。{snapshotMessage}请重启 Steam，从左上角 Steam 菜单打开 Millennium → Plugins，启用 SATLI Achievement Display Bridge；若刚启用，请再重启一次 Steam。SATLI 无需后台运行。"
                    : $"已解除 {selected.Count} 个游戏的 Steam 成就显示覆盖。",
                enable && (pluginUpdated || !pluginActive)
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException(enable ? "锁定 Steam 显示" : "解除 Steam 显示锁定", exception);
        }
        finally
        {
            _operation.Complete();
        }
    }

    private void SchemaMonitor_SchemaChanged(object? sender, SteamSchemaChange change)
    {
        App.DispatcherQueue.TryEnqueue(() => _ = HandleSchemaChangeAsync(change));
    }

    private async Task HandleSchemaChangeAsync(SteamSchemaChange change)
    {
        if (ManagedGames.All(item => item.AppId != change.AppId))
        {
            return;
        }
        await _schemaChangeRefresh.WaitAsync();
        try
        {
            for (var attempt = 0; attempt < 8 && _operation.IsBusy; attempt++)
            {
                await Task.Delay(500);
            }
            if (!_operation.TryBegin())
            {
                return;
            }
            try
            {
                await LoadManagedCoreAsync(forceOffline: true);
                var game = ManagedGames.FirstOrDefault(item => item.AppId == change.AppId);
                if (game is null)
                {
                    return;
                }
                ShowInfo(
                    $"检测到 {game.GameName} 的 Steam 成就 schema 发生外部变化（{Describe(change.ChangeType)}）。"
                    + $" 当前状态：{game.StateText}；{game.ProtectionStatusText}。这可能来自 Steam 或其他程序。",
                    InfoBarSeverity.Warning);
            }
            catch (Exception exception)
            {
                ShowException("监测 Steam 文件变化", exception);
            }
            finally
            {
                _operation.Complete();
            }
        }
        finally
        {
            _schemaChangeRefresh.Release();
        }
    }

    private static string Describe(WatcherChangeTypes changeType) => changeType switch
    {
        WatcherChangeTypes.Created => "重新创建",
        WatcherChangeTypes.Deleted => "删除",
        WatcherChangeTypes.Renamed => "重命名或替换",
        _ => "内容或属性变化",
    };
}
