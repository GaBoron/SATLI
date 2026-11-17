using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.SatliLite;
using Satli_Gui.Controls;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

/// <summary>Installs and explains lite independently of translation selection, while sharing migration prompts.</summary>
internal sealed class LiteSetupService
{
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
        while (true)
        {
            var status = await client.InspectSetupAsync();
            var connection = status.Connection;
            await App.Logs.WriteAsync("信息", "SATLI-lite 安装",
                $"安装状态：Millennium={status.MillenniumInstalled}；lite={status.PluginInstalled}；启用={status.PluginEnabled?.ToString() ?? "未知"}；连接={connection.State}。");
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
                "millennium_missing" => "安装 Millennium 和 lite",
                "legacy_bridge" => "将旧桥移入回收站",
                _ => queue ? "排队发送" : "刷新状态",
            };
            var statusView = new LiteSetupStatusControl();
            statusView.Update(status);
            var content = new StackPanel { Spacing = 16, MinWidth = 340, MaxWidth = 480 };
            content.Children.Add(statusView);
            if (migration.Length > 0) content.Children.Add(new TextBlock { Text = migration.Trim(), TextWrapping = TextWrapping.Wrap });
            if (queue) content.Children.Add(new TextBlock
            {
                Text = "排队请求不会直接覆盖已有译本；发生冲突时，需连接后重新查询并确认。",
                TextWrapping = TextWrapping.Wrap,
            });
            var dialog = new ContentDialog
            {
                XamlRoot = root, Title = requireReady ? "使用 SATLI-lite" : "安装 / 管理 SATLI-lite",
                Content = content,
                PrimaryButtonText = action, CloseButtonText = requireReady ? "暂不应用" : "关闭",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                await App.Logs.WriteAsync("信息", "SATLI-lite 安装", "用户关闭安装或连接提示；未执行后续操作。");
                return false;
            }
            if (queue) return true;
            if (connection.State == "legacy_bridge")
            {
                if (new SteamProcessController().IsRunning())
                    throw new InvalidOperationException("请先正常退出 Steam，再将旧桥移入回收站；原快照、译文和备份会保留。");
                await RunSetupCommandAsync(["lite", "retire-legacy", "--steam-dir", client.SteamDirectory, "--yes", "--jsonl"], settings);
            }
            if (connection.State is "not_installed" or "millennium_missing")
            {
                if (settings.Offline) throw new InvalidOperationException("离线模式下无法首次下载 Millennium 或 SATLI-lite；本地译文和 JSON 缓存保留。");
                var arguments = new List<string> { "lite-install", "--steam-dir", client.SteamDirectory, "--yes", "--jsonl" };
                CliConfiguredPaths.AppendDataDirectory(arguments, settings);
                if (connection.State == "millennium_missing")
                {
                    var installed = await new SteamMutationDialogService().ExecuteAsync(root, async () =>
                    {
                        await RunSetupCommandAsync(arguments.ToArray(), settings);
                        return true;
                    }, "安装 Millennium 需要先退出 Steam。");
                    if (!installed) return false;
                }
                else await RunSetupCommandAsync(arguments.ToArray(), settings);
            }
        }
    }

    private static async Task<string> RunSetupCommandAsync(string[] arguments, GuiSettings settings)
    {
        App.ViewModel.Operation.SetStatus(arguments[0] == "lite-install" ? "正在准备 Millennium 和 SATLI-lite，下载并校验缺少的组件…" : "正在将旧显示桥移入回收站…");
        await App.Logs.WriteAsync("信息", "SATLI-lite 安装", "开始安装或迁移，必要时请求管理员权限。");
        var result = await new SatliCliService().RunAsync(arguments,
            onEvent: item =>
            {
                if (item.Event == "item-progress" && item.Payload.TryGetProperty("message", out var progress))
                    _ = App.Logs.WriteAsync("信息", "SATLI-lite 安装", progress.GetString() ?? "组件处理完成。");
            },
            networkSettings: settings.Network, downloadSourceSettings: settings.DownloadSources);
        if (!result.IsSuccess) throw new InvalidOperationException(result.ErrorMessage);
        var completed = result.Events.LastOrDefault(item => item.Event == "completed");
        var message = completed?.Payload.TryGetProperty("message", out var value) == true ? value.GetString() : null;
        message ??= "安装或迁移操作完成；显示译文仍需另行确认。";
        await App.Logs.WriteAsync("信息", "SATLI-lite 安装", message);
        return message;
    }
}
