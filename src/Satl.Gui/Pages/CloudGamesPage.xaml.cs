using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Satl_Gui.ViewModels;
using Windows.System;

namespace Satl_Gui.Pages;

public sealed partial class CloudGamesPage : Page
{
    private readonly Dictionary<string, GitHubReportDraft> _reportDrafts = [];

    public GameInventoryViewModel ViewModel { get; } = new(GameInventoryScope.Cloud);

    public CloudGamesPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += CloudGamesPage_Loaded;
    }

    private async void CloudGamesPage_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.InitializeAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();

    private async void ViewAchievements_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameItem game })
        {
            return;
        }

        var preview = await App.ViewModel.Translations.PreviewCatalogAsync(game);
        if (preview is not null)
        {
            await ReplacementConfirmationDialog.ShowCatalogReadOnlyAsync(
                XamlRoot,
                [preview],
                $"查看云端成就 · {game.GameName}");
        }
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameItem game })
        {
            return;
        }
        if (App.ViewModel.Settings.Offline)
        {
            App.ViewModel.ShowInfo("离线模式下无法提交 GitHub 报告。", InfoBarSeverity.Warning);
            return;
        }
        try
        {
            if (await App.GitHub.GetAccountAsync() is null)
            {
                var account = await GitHubBindingDialog.BindAsync(XamlRoot, App.GitHub);
                if (account is null)
                {
                    return;
                }
            }
            var draft = await EditReportAsync(game);
            if (draft is null)
            {
                return;
            }
            _reportDrafts[game.AppId] = draft;
            if (!await ConfirmReportAsync(draft))
            {
                return;
            }
            var issueUrl = await App.GitHub.CreateReportIssueAsync(draft);
            _reportDrafts.Remove(game.AppId);
            await ShowSubmittedAsync(issueUrl);
            App.ViewModel.ShowInfo("GitHub 文件错误报告已提交。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"GitHub 报告提交失败：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("GitHub 报告", exception);
        }
    }

    private async Task<GitHubReportDraft?> EditReportAsync(GameItem game)
    {
        _reportDrafts.TryGetValue(game.AppId, out var previous);
        var errorType = new ComboBox
        {
            Header = "错误类型",
            ItemsSource = new[] { "文件可能过期", "文件可能不生效" },
            SelectedIndex = previous?.ErrorType == "文件可能不生效" ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var reason = new TextBox
        {
            Header = "错误说明",
            PlaceholderText = "请写清观察到的变化、日期、版本号或复现信息。",
            Text = previous?.Reason ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
        };
        var reference = new TextBox
        {
            Header = "参考来源（可选）",
            PlaceholderText = $"https://store.steampowered.com/news/app/{game.AppId}/",
            Text = previous?.Reference ?? string.Empty,
        };
        var content = new StackPanel { Spacing = 12, MaxWidth = 560 };
        content.Children.Add(new TextBlock
        {
            Text = $"{game.GameName} · App ID {game.AppId}",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(errorType);
        content.Children.Add(reason);
        content.Children.Add(reference);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "报告成就文件错误",
            Content = content,
            PrimaryButtonText = "预览",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reason.Text),
        };
        reason.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reason.Text);
        dialog.Resources["ContentDialogMaxWidth"] = 640d;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }
        return new GitHubReportDraft(
            game.GameName,
            game.AppId,
            $"https://store.steampowered.com/app/{game.AppId}/",
            errorType.SelectedItem?.ToString() ?? "文件可能过期",
            reason.Text,
            reference.Text);
    }

    private async Task<bool> ConfirmReportAsync(GitHubReportDraft draft)
    {
        GitHubReportFormatter.Validate(draft);
        var preview = new TextBlock
        {
            Text =
                $"{GitHubReportFormatter.Title(draft)}{Environment.NewLine}{Environment.NewLine}" +
                GitHubReportFormatter.Body(draft),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var scroll = new ScrollViewer
        {
            Content = preview,
            MaxHeight = 460,
            MaxWidth = 680,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认提交 GitHub Issue",
            Content = scroll,
            PrimaryButtonText = "提交报告",
            CloseButtonText = "返回修改",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowSubmittedAsync(Uri issueUrl)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "报告已提交",
            Content = new TextBlock
            {
                Text = issueUrl.ToString(),
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "打开 Issue",
            CloseButtonText = "完成",
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Launcher.LaunchUriAsync(issueUrl);
        }
    }
}
