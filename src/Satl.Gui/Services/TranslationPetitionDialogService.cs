using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Windows.System;

namespace Satl_Gui.Services;

public static class TranslationPetitionDialogService
{
    private static readonly Uri ContributionUri = new(
        "https://github.com/GaBoron/steam-achievement-translation-library/issues/new?template=translation_contribution_zh.yml");

    public static async Task RunAsync(XamlRoot xamlRoot, GameItem? game = null)
    {
        if (App.ViewModel.Settings.Offline)
        {
            App.ViewModel.ShowInfo("离线模式下无法提交 GitHub 翻译请愿。", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var account = await App.GitHub.GetAccountAsync();
            var input = await EditAsync(xamlRoot, game, account);
            if (input is null)
            {
                return;
            }

            var workflow = new TranslationPetitionWorkflowService();
            var normalized = workflow.NormalizeInput(input);
            var output = PickDestination(normalized.AppId);
            if (output is null
                || !await App.ViewModel.Translations.ExportPetitionAsync(normalized.AppId, output))
            {
                return;
            }

            var draft = workflow.Prepare(normalized, output);
            await workflow.OpenAsync(draft);
            App.ViewModel.ShowInfo(
                "翻译请愿表单已自动填写，ZIP 也已在资源管理器中选中；请将 ZIP 拖到上传区域后提交 Issue。",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法准备翻译请愿：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("翻译请愿", exception);
        }
    }

    private static async Task<TranslationPetitionInput?> EditAsync(
        XamlRoot xamlRoot,
        GameItem? game,
        GitHubAccount? account)
    {
        var workflow = new TranslationPetitionWorkflowService();
        var gameName = new TextBox
        {
            Header = "游戏名",
            PlaceholderText = "Steam 商店中的游戏名",
            Text = game?.GameName ?? string.Empty,
            MaxLength = 200,
        };
        var appId = new TextBox
        {
            Header = "Steam App ID",
            PlaceholderText = "例如：123456",
            Text = game?.AppId ?? string.Empty,
            IsReadOnly = game is not null,
            MaxLength = 20,
        };
        var targetLanguages = new TextBox
        {
            Header = "希望翻译到的语言",
            PlaceholderText = "schinese",
            Text = "schinese",
        };
        var notes = new TextBox
        {
            Header = "备注（可选）",
            PlaceholderText = "可填写术语偏好、参考资料或其他希望译者注意的内容。",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
        };
        AutomationProperties.SetName(gameName, "游戏名");
        AutomationProperties.SetName(appId, "Steam App ID");
        AutomationProperties.SetName(targetLanguages, "希望翻译到的语言");
        AutomationProperties.SetName(notes, "翻译请愿备注");

        var validation = new TextBlock
        {
            Foreground = Application.Current.Resources["SystemFillColorCriticalBrush"] as Microsoft.UI.Xaml.Media.Brush,
            TextWrapping = TextWrapping.Wrap,
        };
        var content = new StackPanel { Spacing = 12, MaxWidth = 560 };
        content.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = account is null ? InfoBarSeverity.Informational : InfoBarSeverity.Success,
            Title = account is null ? "使用 GitHub 网页表单" : $"已绑定 GitHub：@{account.Login}",
            Message = "应用会导出并校验原始 schema ZIP，自动填写 Issue 字段并打开附件位置。"
                + "GitHub Issue API 不支持附件上传，最后只需把选中的 ZIP 拖到上传区域并提交。",
        });
        content.Children.Add(gameName);
        content.Children.Add(appId);
        content.Children.Add(targetLanguages);
        content.Children.Add(notes);
        content.Children.Add(validation);
        var contributionButton = new Button
        {
            Content = "已经完成翻译？改为贡献翻译",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        contributionButton.Click += async (_, _) => await Launcher.LaunchUriAsync(ContributionUri);
        content.Children.Add(contributionButton);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "请求社区翻译",
            Content = content,
            PrimaryButtonText = "导出 ZIP 并准备 Issue",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 660d;

        TranslationPetitionInput ReadInput() => new(
            gameName.Text,
            appId.Text,
            targetLanguages.Text,
            notes.Text);
        void UpdateReady() => dialog.IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(gameName.Text)
            && !string.IsNullOrWhiteSpace(appId.Text)
            && !string.IsNullOrWhiteSpace(targetLanguages.Text);
        gameName.TextChanged += (_, _) => UpdateReady();
        appId.TextChanged += (_, _) => UpdateReady();
        targetLanguages.TextChanged += (_, _) => UpdateReady();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                workflow.NormalizeInput(ReadInput());
                validation.Text = string.Empty;
            }
            catch (ArgumentException exception)
            {
                args.Cancel = true;
                validation.Text = exception.Message;
            }
        };
        UpdateReady();
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? workflow.NormalizeInput(ReadInput())
            : null;
    }

    private static string? PickDestination(string appId) =>
        NativeFilePickerService.PickSaveFile(
            App.WindowHandle,
            "导出翻译请愿 ZIP",
            $"UserGameStatsSchema_{appId}.zip",
            "ZIP 压缩文件",
            ".zip");
}
