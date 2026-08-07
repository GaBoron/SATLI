using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Satli_Gui.Services;

public static class GitHubBindingDialog
{
    public static async Task<GitHubAccount?> BindAsync(
        XamlRoot xamlRoot,
        GitHubIntegrationService service,
        CancellationToken cancellationToken = default)
    {
        var challenge = await service.StartDeviceFlowAsync(cancellationToken);
        var codeText = new TextBlock
        {
            Text = challenge.UserCode,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var content = new StackPanel { Spacing = 12, MaxWidth = 460 };
        content.Children.Add(new TextBlock
        {
            Text = "在 GitHub 授权页输入下面的一次性代码。代码约 15 分钟后失效。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(codeText);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "绑定 GitHub",
            Content = content,
            PrimaryButtonText = "复制代码并打开 GitHub",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }
        var package = new DataPackage();
        package.SetText(challenge.UserCode);
        Clipboard.SetContent(package);
        await Launcher.LaunchUriAsync(challenge.VerificationUri);
        return await service.CompleteDeviceFlowAsync(challenge, cancellationToken);
    }
}
