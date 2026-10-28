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
                    + $" 当前 BIN 状态：{game.StateText}。可选择通过 SATLI-lite 管理独立 JSON 显示；无需继续修改 BIN。",
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
