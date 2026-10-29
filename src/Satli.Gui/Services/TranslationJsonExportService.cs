using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Satli_Gui.Services;

public sealed record TranslationJsonDestination(string Output, string VariantId);

/// <summary>Shared variant selection, destination and read-only export flow.</summary>
public static partial class TranslationJsonExportService
{
    public static async Task<TranslationJsonDestination?> PickAsync(XamlRoot root, string appId, string variantId)
    {
        await App.Logs.WriteAsync("信息", "翻译 JSON", "开始准备独立翻译 JSON 导出。");
        var variantBox = new TextBox
        {
            Header = "译本版本 ID（variant_id）",
            Text = string.IsNullOrWhiteSpace(variantId) ? "default" : variantId,
            MaxLength = 64,
        };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "导出当前数据中的全部语言，按成就 API name 对应。名称和描述分别校验：所有语言均有内容，或全部为空。JSON 仅含显示文本，不能取代完整 Steam BIN。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(variantBox);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = $"导出翻译 JSON · {appId}",
            Content = content,
            PrimaryButtonText = "选择保存位置",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!VariantPattern().IsMatch(variantBox.Text))
            {
                args.Cancel = true;
                error.Text = "版本 ID 必须为 1–64 个小写字母、数字或连字符，首字符不能为连字符；版本与语言分别设置。";
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            await App.Logs.WriteAsync("信息", "翻译 JSON", "用户取消 JSON 导出。");
            return null;
        }
        var output = NativeFilePickerService.PickSaveFile(App.WindowHandle, "导出翻译 JSON",
            $"UserGameStatsSchema_{appId}.json", "翻译 JSON", ".json");
        if (output is null)
        {
            await App.Logs.WriteAsync("信息", "翻译 JSON", "用户取消选择 JSON 保存位置。");
            return null;
        }
        return new(output, variantBox.Text);
    }

    public static async Task ExportAsync(XamlRoot root, string appId, string variantId, List<string> arguments)
    {
        try
        {
            var destination = await PickAsync(root, appId, variantId);
            if (destination is null) return;
            App.ViewModel.ShowInfo("正在校验并导出翻译 JSON…", InfoBarSeverity.Informational);
            arguments.AddRange(["--format", "json", "--output", destination.Output,
                "--variant-id", destination.VariantId, "--jsonl"]);
            var settings = App.ViewModel.Settings;
            CliConfiguredPaths.AppendSteamDirectory(arguments, settings,
                App.ViewModel.Translations.DetectedSteamDirectory);
            CliConfiguredPaths.AppendDataDirectory(arguments, settings);
            var result = await Task.Run(() => new SatliCliService().RunAsync(arguments,
                networkSettings: settings.Network, downloadSourceSettings: settings.DownloadSources));
            if (!result.IsSuccess) throw new InvalidDataException(result.ErrorMessage);
            await LogCompletedAsync(appId, destination);
            App.ViewModel.ShowInfo($"已导出翻译 JSON：{destination.Output}", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            await ReportFailureAsync(exception);
        }
    }

    public static async Task LogCompletedAsync(string appId, TranslationJsonDestination destination)
    {
        await App.Logs.WriteAsync("信息", "翻译 JSON", "翻译 JSON 已通过校验并导出。");
        await App.Logs.WriteAsync("详细", "翻译 JSON",
            $"App ID={appId}；Variant={destination.VariantId}；文件={Path.GetFileName(destination.Output)}。", detailed: true);
    }

    public static async Task ReportFailureAsync(Exception exception)
    {
        App.ViewModel.ShowInfo($"翻译 JSON 导出失败：{exception.Message}", InfoBarSeverity.Error);
        await App.Logs.WriteAsync("警告", "翻译 JSON", "翻译 JSON 未能导出，请检查界面中的校验结果。");
        await App.Logs.WriteAsync("详细", "翻译 JSON",
            $"异常类型={exception.GetType().Name}；HRESULT=0x{exception.HResult:X8}。", detailed: true);
        await App.Logs.WriteAsync("调试", "翻译 JSON", exception.ToString(), debug: true);
    }

    [GeneratedRegex("\\A[a-z0-9][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VariantPattern();
}
