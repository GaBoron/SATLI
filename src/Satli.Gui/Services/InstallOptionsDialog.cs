using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Satli_Gui.Services;

public sealed record InstallOptions(bool LockAfterInstall);

public static class InstallOptionsDialog
{
    public static async Task<InstallOptions?> ShowAsync(
        XamlRoot xamlRoot,
        int gameCount,
        int alreadyLockedCount)
    {
        var lockAfterInstall = new CheckBox
        {
            Content = "安装完成后，将尚未锁定的完整成就 schema 强制设为只读",
        };
        var riskNotice = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Title = "此选项风险巨大",
            Message = "下一步仍会要求单独确认风险；Steam 可能绕过只读属性或因锁定而工作异常。",
        };
        lockAfterInstall.Checked += (_, _) => riskNotice.IsOpen = true;
        lockAfterInstall.Unchecked += (_, _) => riskNotice.IsOpen = false;

        var summary = alreadyLockedCount == 0
            ? $"即将安装或更新 {gameCount} 个翻译。"
            : $"即将安装或更新 {gameCount} 个翻译，其中 {alreadyLockedCount} 个文件会保留现有锁定。";
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "安装选项",
            PrimaryButtonText = "开始安装",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 560,
                Children =
                {
                    new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap },
                    lockAfterInstall,
                    riskNotice,
                },
            },
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? new InstallOptions(lockAfterInstall.IsChecked == true)
            : null;
    }
}
