using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

public static class SteamFileProtectionDialog
{
    public static async Task<bool> ConfirmLockAsync(
        XamlRoot xamlRoot,
        IReadOnlyList<GameItem> games)
    {
        var acknowledgement = new CheckBox
        {
            Content = "我理解这可能导致 Steam 更新、校验、同步或成就显示异常",
        };
        AutomationProperties.SetName(acknowledgement, "确认理解强制锁定的巨大风险");
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "巨大风险：强制锁定 Steam 成就文件",
            PrimaryButtonText = $"仍要锁定 {games.Count} 个文件",
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
                        Severity = InfoBarSeverity.Error,
                        Title = "这不是可靠的安全边界",
                        Message = "Steam 可以清除只读属性或重建整个文件；锁定也可能妨碍 Steam 正常工作。",
                    },
                    new TextBlock
                    {
                        Text = "只读属性作用于每个游戏的整份成就 schema，不是某一条成就。继续前应从系统托盘完全退出 Steam；发生异常时请立即解除锁定。",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "SATLI 会监测文件覆盖、删除、重命名和属性变化，但监测只能提醒，不能阻止或撤销 Steam 的行为。",
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
}
