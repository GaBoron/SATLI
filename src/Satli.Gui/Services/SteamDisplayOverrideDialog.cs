using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.SteamDisplay;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

public static class SteamDisplayOverrideDialog
{
    public static Uri MillenniumInstallationUri { get; } = new(
        "https://docs.steambrew.app/users/getting-started/installation");

    public static async Task<bool> EnsureMillenniumInstalledAsync(
        XamlRoot xamlRoot,
        string steamDirectory)
    {
        if (SteamDisplayPluginInstaller.IsMillenniumInstalled(steamDirectory))
        {
            return true;
        }

        await App.Logs.WriteAsync(
            "警告",
            "Steam 显示覆盖",
            "锁定已暂停：当前 Steam 目录未检测到完整的 Millennium 安装。");
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "需要先安装 Millennium",
            PrimaryButtonText = "打开 Millennium 官方安装页",
            CloseButtonText = "暂不安装",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 560,
                Children =
                {
                    new InfoBar
                    {
                        IsOpen = true,
                        IsClosable = false,
                        Severity = InfoBarSeverity.Informational,
                        Title = "SATLI 不会下载或安装 Millennium",
                        Message = "请只从下方打开的 Steam Homebrew 官方页面获取带数字签名的 Windows 安装器。",
                    },
                    new TextBlock
                    {
                        Text = "1. 完全退出 Steam。\n"
                            + "2. 在官方页面下载并运行 MillenniumInstaller-Windows.exe，按安装器提示完成安装。\n"
                            + "3. 启动一次 Steam，确认左上角 Steam 菜单中出现 Millennium。\n"
                            + "4. 返回 SATLI，再次点击“锁定”。",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "SATLI 检测到 Millennium 后才会部署自己的显示插件；取消此对话框不会修改 Steam。",
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                var opened = await Windows.System.Launcher.LaunchUriAsync(
                    MillenniumInstallationUri);
                await App.Logs.WriteAsync(
                    opened ? "信息" : "警告",
                    "Steam 显示覆盖",
                    opened
                        ? "已打开 Millennium 官方安装页。"
                        : "系统未能打开 Millennium 官方安装页。");
            }
            catch (Exception exception)
            {
                await App.Logs.WriteAsync(
                    "错误",
                    "Steam 显示覆盖",
                    $"打开 Millennium 官方安装页失败：{exception.Message}");
                await App.Logs.WriteExceptionDetailsAsync("Steam 显示覆盖", exception);
            }
        }
        else
        {
            await App.Logs.WriteAsync(
                "信息",
                "Steam 显示覆盖",
                "用户暂不安装 Millennium，锁定未执行。");
        }
        return false;
    }

    public static async Task<bool> ConfirmLockAsync(
        XamlRoot xamlRoot,
        string steamDirectory,
        IReadOnlyList<GameItem> games)
    {
        var plugin = SteamDisplayPluginInstaller.Inspect(
            steamDirectory,
            SteamDisplayPluginInstaller.BundledPluginPath());
        var pluginStatus = DescribePluginStatus(plugin);
        var acknowledgement = new CheckBox
        {
            Content = "我理解这依赖第三方 Millennium 插件，并可能随 Steam 更新失效",
        };
        AutomationProperties.SetName(acknowledgement, "确认理解 Steam 显示覆盖的兼容性风险");
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "锁定 Steam 成就显示",
            PrimaryButtonText = $"锁定 {games.Count} 个游戏的显示",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 560,
                Children =
                {
                    new InfoBar
                    {
                        IsOpen = true,
                        IsClosable = false,
                        Severity = pluginStatus.Severity,
                        Title = pluginStatus.Title,
                        Message = pluginStatus.Message,
                    },
                    new TextBlock
                    {
                        Text = "该方案不会修改或拦截 Steam 服务器响应，而是在界面渲染后替换成就名称和描述。Steam 更新可能暂时破坏兼容性。",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "覆盖目标包括游戏库、活动动态和成就弹窗等 Steam 页面；未识别或发生歧义的文本会保留原样，少数与 Steam 普通界面完全同名的文本也可能被误替换。",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "SATLI 只写入静态翻译快照，完成后可以彻底退出；无需开启 SATLI 自启、托盘运行或后台常驻。",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    acknowledgement,
                },
            },
        };
        acknowledgement.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        acknowledgement.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static (string Title, string Message, InfoBarSeverity Severity)
        DescribePluginStatus(SteamDisplayPluginStatus plugin)
    {
        if (!plugin.Installed)
        {
            return (
                "将首次部署 SATLI 插件",
                "锁定完成后请重启 Steam，从左上角 Steam 菜单打开 Millennium → Plugins，启用 SATLI Achievement Display Bridge；如果刚启用，请再重启一次 Steam。",
                InfoBarSeverity.Informational);
        }
        if (!plugin.Current)
        {
            return (
                "将更新 SATLI 插件",
                "本次锁定会替换插件文件。完成后需要重启 Steam；原有启用状态通常会保留。",
                InfoBarSeverity.Informational);
        }
        if (plugin.RuntimeActive)
        {
            return (
                "SATLI 插件正在运行",
                "本次锁定会自动刷新显示，通常在约 2 秒内生效；无需重新打开插件设置，也无需重启 Steam。",
                InfoBarSeverity.Success);
        }
        return (
            "SATLI 插件当前未运行",
            "如果 Steam 已退出，锁定后正常启动即可；如果 Steam 正在运行，请在 Millennium → Plugins 中确认插件已启用，然后重启 Steam。",
            InfoBarSeverity.Warning);
    }
}
