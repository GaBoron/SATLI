using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.SatliLite;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

/// <summary>Installs and explains lite independently of translation selection, while sharing migration prompts.</summary>
internal sealed class LiteSetupService
{
    private const string ManagementGuide = "在 Steam → Millennium → Plugins 启用 SATLI-lite，然后完全退出并重启 Steam。"
        + "在 Steam 的 Millennium 插件设置中打开 SATLI lite，管理译本、语言及显示开关。"
        + "已有插件的更新由 lite 自身负责。安装插件不会自动导入 SATLI 的译文或旧锁定快照。";

    public async Task ManageAsync(XamlRoot root, GuiSettings settings)
    {
        var client = await Task.Run(() => LiteWorkflowService.Client(settings));
        await PromptAsync(root, client, settings, requireReady: false);
    }

    public Task<bool> EnsureReadyAsync(XamlRoot root, LiteClient client, GuiSettings settings, string appId) =>
        PromptAsync(root, client, settings, requireReady: true, appId);

    private static async Task<bool> PromptAsync(XamlRoot root, LiteClient client, GuiSettings settings,
        bool requireReady, string? appId = null)
    {
        var lastOperation = "";
        while (true)
        {
            var connection = await client.InspectAsync();
            await App.Logs.WriteAsync("信息", "SATLI-lite 安装", $"连接状态：{connection.State}。");
            if (requireReady && connection.Connected) return true;
            var migration = "";
            if (connection.State == "legacy_bridge")
            {
                var ids = await client.LegacyAppIdsAsync();
                migration = appId is not null && ids.Contains(appId)
                    ? "\n发现此游戏的旧显示锁定记录。将使用已预览译文通过新协议迁移；旧桥快照不会直接导入。"
                    : $"\n旧桥记录了 {ids.Count} 个游戏。移除安装包会保留这些快照，可随后逐个选择可信译文发送到 lite。";
                await App.Logs.WriteAsync("信息", "SATLI-lite 安装", $"检测到旧桥迁移记录。游戏数={ids.Count}。");
            }
            var queue = requireReady && connection.CanSubmit;
            var action = connection.State switch
            {
                "not_installed" => "下载并安装 lite",
                "millennium_missing" => "打开 Millennium 官方指南",
                "legacy_bridge" => "将旧桥移入回收站",
                _ => queue ? "排队发送" : "刷新状态",
            };
            var dialog = new ContentDialog
            {
                XamlRoot = root, Title = requireReady ? "使用 SATLI-lite" : "安装 / 管理 SATLI-lite",
                Content = new TextBlock
                {
                    Text = (lastOperation.Length == 0 ? "" : lastOperation + "\n\n")
                        + connection.Message + migration + "\n\n" + ManagementGuide
                        + (queue ? "\n排队请求默认拒绝覆盖已有译本；发生冲突时需连接后重新查询和确认。" : ""),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = action, CloseButtonText = requireReady ? "暂不应用" : "关闭",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                await App.Logs.WriteAsync("信息", "SATLI-lite 安装", "用户关闭安装或连接提示；未执行后续操作。");
                return false;
            }
            if (queue) return true;
            if (connection.State == "millennium_missing")
            {
                await new ExternalUriLauncher().LaunchAsync(new Uri("https://docs.steambrew.app/users/getting-started/installation"));
                return false;
            }
            if (connection.State == "legacy_bridge")
            {
                if (new SteamProcessController().IsRunning())
                    throw new InvalidOperationException("请先正常退出 Steam，再将旧桥移入回收站；原快照、译文和备份会保留。");
                lastOperation = await RunSetupCommandAsync(["lite", "retire-legacy", "--steam-dir", client.SteamDirectory, "--yes", "--jsonl"], settings);
            }
            if (connection.State == "not_installed")
            {
                if (settings.Offline) throw new InvalidOperationException("离线模式下无法首次下载 SATLI-lite；本地译文和 JSON 缓存保留。");
                lastOperation = await RunSetupCommandAsync(["lite-install", "--steam-dir", client.SteamDirectory, "--yes", "--jsonl"], settings);
            }
        }
    }

    private static async Task<string> RunSetupCommandAsync(string[] arguments, GuiSettings settings)
    {
        App.ViewModel.Operation.SetStatus(arguments[0] == "lite-install" ? "正在下载并校验 SATLI-lite…" : "正在将旧显示桥移入回收站…");
        await App.Logs.WriteAsync("信息", "SATLI-lite 安装", "开始安装或迁移，必要时请求管理员权限。");
        var result = await new SatliCliService().RunAsync(arguments,
            networkSettings: settings.Network, downloadSourceSettings: settings.DownloadSources);
        if (!result.IsSuccess) throw new InvalidOperationException(result.ErrorMessage);
        var completed = result.Events.LastOrDefault(item => item.Event == "completed");
        var message = completed?.Payload.TryGetProperty("message", out var value) == true ? value.GetString() : null;
        message ??= "安装或迁移操作完成；显示译文仍需另行确认。";
        await App.Logs.WriteAsync("信息", "SATLI-lite 安装", message);
        return message;
    }
}
