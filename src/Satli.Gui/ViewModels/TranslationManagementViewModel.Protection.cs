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
                enable ? "正在强制锁定 Steam 成就文件…" : "正在解除 Steam 成就文件锁定…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await LoadManagedCoreAsync(forceOffline: true);
            ShowInfo(
                enable
                    ? $"已将 {selected.Count} 个完整 schema 设为只读。此保护风险巨大且可能被 Steam 绕过。"
                    : $"已解除 {selected.Count} 个 schema 的只读锁定。",
                enable ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException(enable ? "强制锁定" : "解除锁定", exception);
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
