using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.Steam;
using Satli.Core.SteamDisplay;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

internal sealed class SteamDisplayPluginStartupUpdater
{
    private const int MarkerVersion = 1;
    private readonly string _markerPath;
    private readonly string _bundledPluginPath;

    public SteamDisplayPluginStartupUpdater(
        string? markerPath = null,
        string? bundledPluginPath = null)
    {
        _markerPath = markerPath ?? Path.Combine(
            ApplicationDataPaths.DefaultDataDirectory,
            "steam-display-plugin-sync.json");
        _bundledPluginPath = bundledPluginPath
            ?? SteamDisplayPluginInstaller.BundledPluginPath();
    }

    public async Task RunAsync(GuiSettings settings, LogService logs)
    {
        try
        {
            var bundledPlugin = _bundledPluginPath;
            if (!File.Exists(bundledPlugin))
            {
                await logs.WriteAsync(
                    "警告",
                    "Steam 显示覆盖",
                    "启动时无法检查插件更新：当前 SATLI 构建缺少内置显示插件。");
                return;
            }

            var bundledHash = await Task.Run(() => FileOperations.Sha256(bundledPlugin));
            if (MarkerMatches(bundledHash))
            {
                await logs.WriteAsync(
                    "详细",
                    "Steam 显示覆盖",
                    "内置显示插件与上次启动检查相同，跳过 Steam 探测。",
                    detailed: true);
                return;
            }

            var steamDirectory = await Task.Run(() => SteamLocator.FindSteamDirectory(
                string.IsNullOrWhiteSpace(settings.SteamDirectory)
                    ? null
                    : settings.SteamDirectory));
            var overrides = new SteamDisplayOverrideStore(
                SteamDisplayPluginInstaller.BridgePath(steamDirectory));
            if (!File.Exists(overrides.BridgePath) || !overrides.HasEnabledOverrides)
            {
                WriteMarker(bundledHash);
                await logs.WriteAsync(
                    "详细",
                    "Steam 显示覆盖",
                    "未发现已启用的显示锁定；本版本不检查或部署插件。",
                    detailed: true);
                return;
            }

            await logs.WriteAsync(
                "信息",
                "Steam 显示覆盖",
                "发现已启用的显示锁定，开始后台检查内置插件更新。");
            var status = await Task.Run(() => SteamDisplayPluginInstaller.Inspect(
                steamDirectory,
                bundledPlugin));
            if (status.Current)
            {
                WriteMarker(bundledHash);
                await logs.WriteAsync(
                    "信息",
                    "Steam 显示覆盖",
                    "已安装的显示插件与当前 SATLI 内置版本一致。");
                return;
            }

            var result = await Task.Run(() => SteamDisplayPluginInstaller.EnsureInstalled(
                steamDirectory,
                bundledPlugin));
            WriteMarker(bundledHash);
            await logs.WriteAsync(
                "信息",
                "Steam 显示覆盖",
                result.RuntimeActive
                    ? "已在后台静默升级显示插件；Steam 下次重启后将完整使用新版插件。"
                    : "已在后台静默升级显示插件。下次启动 Steam 时将使用新版插件。");
        }
        catch (Exception exception)
        {
            await logs.WriteAsync(
                "警告",
                "Steam 显示覆盖",
                $"后台检查或升级显示插件失败：{exception.Message}");
            await logs.WriteExceptionDetailsAsync("Steam 显示覆盖", exception);
        }
    }

    private bool MarkerMatches(string bundledHash)
    {
        if (!File.Exists(_markerPath))
        {
            return false;
        }
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_markerPath))?.AsObject();
            return root?["version"]?.GetValue<int>() == MarkerVersion
                && string.Equals(
                    root["bundled_sha256"]?.GetValue<string>(),
                    bundledHash,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private void WriteMarker(string bundledHash)
    {
        var marker = new JsonObject
        {
            ["version"] = MarkerVersion,
            ["bundled_sha256"] = bundledHash,
            ["checked_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(marker);
        FileOperations.WriteDurable(_markerPath, [.. payload, (byte)'\n']);
    }
}
