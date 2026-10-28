using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Services;

namespace Satli_Gui.Pages;

public sealed partial class SettingsPage
{
    private int _liteInspection;

    private async Task RefreshLiteDescriptionAsync()
    {
        var inspection = ++_liteInspection;
        var settings = ViewModel.Settings;
        try
        {
            var connection = await Task.Run(async () => await LiteWorkflowService.Client(settings).InspectAsync());
            if (inspection == _liteInspection) LiteStatusText.Text = connection.Message;
        }
        catch (Exception exception)
        {
            if (inspection == _liteInspection) LiteStatusText.Text = "尚未检测到可用安装，请检查 Steam 目录后打开管理入口。";
            await App.Logs.WriteAsync("警告", "SATLI-lite 安装", "设置页安装检测未完成；可检查 Steam 目录后重试。");
            await App.Logs.WriteExceptionDetailsAsync("SATLI-lite 安装", exception);
        }
    }

    private async void ManageLite_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.Operation.TryBegin()) return;
        ManageLiteButton.IsEnabled = false;
        try
        {
            ViewModel.Operation.SetStatus("正在检查 SATLI-lite 安装…");
            await App.Logs.WriteAsync("信息", "SATLI-lite 安装", "打开独立安装与管理入口。");
            await new LiteSetupService().ManageAsync(XamlRoot, ViewModel.Settings);
        }
        catch (OperationCanceledException)
        {
            await App.Logs.WriteAsync("信息", "SATLI-lite 安装", "用户取消安装或迁移；已有文件保留。");
            ViewModel.ShowInfo("已取消安装或迁移；已有文件保留。", InfoBarSeverity.Warning);
        }
        catch (Exception exception)
        {
            await App.Logs.WriteAsync("错误", "SATLI-lite 安装", "安装或迁移未完成；已有安装、译文和备份保留。");
            await App.Logs.WriteExceptionDetailsAsync("SATLI-lite 安装", exception);
            ViewModel.ShowInfo(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            ManageLiteButton.IsEnabled = true;
            ViewModel.Operation.Complete();
            await RefreshLiteDescriptionAsync();
        }
    }
}
