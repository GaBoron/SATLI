using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Satli_Gui.Services;

public sealed class SteamMutationDialogService
{
    private readonly SteamMutationWorkflow _workflow;

    public SteamMutationDialogService(ISteamProcessController? steam = null)
    {
        _workflow = new SteamMutationWorkflow(steam ?? new SteamProcessController());
    }

    public async Task<bool> ExecuteAsync(XamlRoot xamlRoot, Func<Task<bool>> operationAsync)
    {
        SteamMutationOutcome outcome;
        try
        {
            outcome = await _workflow.ExecuteAsync(
                async () =>
                {
                    var choice = await ShowChoiceAsync(xamlRoot);
                    await App.Logs.WriteAsync(
                        "详细",
                        "Steam 进程控制",
                        $"Steam 正在运行；用户选择={Describe(choice)}。",
                        detailed: true);
                    return choice;
                },
                operationAsync);
        }
        catch (SteamProcessOperationException exception)
        {
            App.ViewModel.ShowInfo(exception.Message, InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("Steam 进程控制", exception);
            return false;
        }

        await App.Logs.WriteAsync(
            "详细",
            "Steam 进程控制",
            $"流程结束。操作已开始={outcome.OperationStarted}；操作成功={outcome.OperationSucceeded}；" +
            $"Steam 已重启={outcome.SteamRestarted}。",
            detailed: true);
        if (!outcome.OperationStarted)
        {
            await App.Logs.WriteAsync("信息", "Steam 进程控制", "用户已取消需要关闭 Steam 的操作。");
        }
        else if (outcome.SteamRestarted)
        {
            await App.Logs.WriteAsync("信息", "Steam 进程控制", "操作成功，Steam 已在后台重新启动。");
        }
        if (!string.IsNullOrWhiteSpace(outcome.RestartWarning))
        {
            App.ViewModel.ShowInfo(outcome.RestartWarning, InfoBarSeverity.Warning);
            await App.Logs.WriteAsync("警告", "Steam 进程控制", outcome.RestartWarning);
        }
        return outcome.OperationSucceeded;
    }

    private static string Describe(SteamRunningChoice choice) => choice switch
    {
        SteamRunningChoice.ForceClose => "强制关闭",
        SteamRunningChoice.CloseAndRestart => "关闭并重启",
        _ => "取消",
    };

    private static async Task<SteamRunningChoice> ShowChoiceAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Steam 正在运行",
            Content = new TextBlock
            {
                Text = "Steam 可能覆盖成就文件。“关闭并重启”仅在操作成功后于后台重启 Steam。",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "强制关闭",
            SecondaryButtonText = "关闭并重启",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => SteamRunningChoice.ForceClose,
            ContentDialogResult.Secondary => SteamRunningChoice.CloseAndRestart,
            _ => SteamRunningChoice.Cancel,
        };
    }
}
