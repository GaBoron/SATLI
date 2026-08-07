using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Satli_Gui.Services;
using Windows.System;

namespace Satli_Gui.Controls;

public sealed partial class GitHubSettingsControl : UserControl
{
    public GitHubSettingsControl()
    {
        InitializeComponent();
        Loaded += GitHubSettingsControl_Loaded;
    }

    private async void GitHubSettingsControl_Loaded(object sender, RoutedEventArgs e) =>
        await RefreshAsync();

    private async Task RefreshAsync()
    {
        var account = await App.GitHub.GetAccountAsync();
        if (!App.GitHub.IsConfigured)
        {
            AccountStatusText.Text =
                "此构建未配置 GitHub App，无法进行账户绑定。";
            BindButton.IsEnabled = false;
        }
        else
        {
            AccountStatusText.Text = account is null
                ? "GitHub App 已配置，尚未绑定账户。"
                : $"已绑定：@{account.Login}";
            BindButton.IsEnabled = true;
        }
        BindButton.Content = account is null ? "绑定 GitHub" : "重新绑定";
        UnbindButton.Visibility = account is null ? Visibility.Collapsed : Visibility.Visible;
        AccountAvatar.Visibility = account is null || string.IsNullOrWhiteSpace(account.AvatarUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
        AccountAvatar.Source = account is null || string.IsNullOrWhiteSpace(account.AvatarUrl)
            ? null
            : new BitmapImage(new Uri(account.AvatarUrl));
    }

    private async void Bind_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var account = await GitHubBindingDialog.BindAsync(XamlRoot, App.GitHub);
            if (account is not null)
            {
                App.ViewModel.ShowInfo($"已绑定 GitHub：@{account.Login}", InfoBarSeverity.Success);
            }
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"GitHub 绑定失败：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("GitHub 绑定", exception);
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async void Unbind_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "解绑 GitHub",
            Content = "这会删除本机加密凭据。如需撤销服务器端授权，请同时在 GitHub 授权设置中操作。",
            PrimaryButtonText = "解绑",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }
        App.GitHub.Unbind();
        await RefreshAsync();
        App.ViewModel.ShowInfo("已删除本机 GitHub 绑定。", InfoBarSeverity.Success);
    }

    private async void ManageAuthorization_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://github.com/settings/applications"));

    private void SetBusy(bool busy)
    {
        BindButton.IsEnabled = !busy && App.GitHub.IsConfigured;
        UnbindButton.IsEnabled = !busy;
        AccountStatusText.Text = busy ? "正在等待 GitHub 授权…" : AccountStatusText.Text;
    }
}
