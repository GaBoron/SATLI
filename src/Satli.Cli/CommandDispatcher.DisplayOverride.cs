using System.Text.Json.Nodes;
using Satli.Core;
using Satli.Core.State;
using Satli.Core.SteamDisplay;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private void RefreshDisplayOverride(
        string operation,
        string appId,
        string gameName,
        string schemaPath,
        string steamDirectory,
        string dataDirectory)
    {
        try
        {
            var store = new SteamDisplayOverrideStore(
                SteamDisplayPluginInstaller.BridgePath(steamDirectory));
            if (!store.IsEnabled(appId))
            {
                return;
            }
            var registry = new ManagedGameRegistry(dataDirectory, steamDirectory);
            var original = DisplayOverrideBackup(operation, registry, appId, schemaPath);
            store.Enable(
                appId,
                gameName,
                schemaPath,
                original is null ? [] : [original]);
            _events.Emit(operation, "display-override-refreshed", new JsonObject
            {
                ["app_id"] = appId,
                ["bridge_path"] = store.BridgePath,
            });
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SatliException)
        {
            _events.Emit(operation, "warning", new JsonObject
            {
                ["app_id"] = appId,
                ["message"] = $"译文已保存，但 Steam 显示覆盖刷新失败：{exception.Message}",
            });
        }
    }

    private string? DisplayOverrideBackup(
        string operation,
        ManagedGameRegistry registry,
        string appId,
        string schemaPath)
    {
        try
        {
            return registry.RestorePreviewSource(appId, schemaPath);
        }
        catch (SatliException exception)
        {
            _events.Emit(operation, "warning", new JsonObject
            {
                ["app_id"] = appId,
                ["message"] = "未能验证安装前备份；显示覆盖将使用当前译文中的全部语言作为源文本。",
                ["error_type"] = exception.GetType().Name,
            });
            return null;
        }
    }

    private void DisableDisplayOverride(
        string operation,
        string appId,
        string steamDirectory)
    {
        try
        {
            var store = new SteamDisplayOverrideStore(
                SteamDisplayPluginInstaller.BridgePath(steamDirectory));
            if (!store.IsEnabled(appId))
            {
                return;
            }
            store.Disable(appId);
            _events.Emit(operation, "display-override-disabled", new JsonObject
            {
                ["app_id"] = appId,
                ["bridge_path"] = store.BridgePath,
            });
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SatliException)
        {
            _events.Emit(operation, "warning", new JsonObject
            {
                ["app_id"] = appId,
                ["message"] = $"文件已恢复，但 Steam 显示覆盖解除失败：{exception.Message}",
            });
        }
    }
}
