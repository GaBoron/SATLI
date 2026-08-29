using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;
using Satli_Gui.Services;
using Satli_Gui.ViewModels;

namespace Satli_Gui.Pages;

public sealed partial class CloudGamesPage : Page
{
    private readonly Dictionary<string, GitHubReportDraft> _reportDrafts = [];
    private readonly GitHubReportWorkflowService _reports = new();

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

        var previews = await App.ViewModel.Translations.PreviewCatalogAsync(game);
        if (previews is { Count: > 0 })
        {
            await ReplacementConfirmationDialog.ShowCatalogReadOnlyAsync(
                XamlRoot,
                previews,
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
            await App.Logs.WriteAsync(
                "信息",
                "GitHub 报告",
                $"开始准备文件错误报告。App ID={game.AppId}。");
            var draft = await EditReportAsync(game);
            if (draft is null)
            {
                await App.Logs.WriteAsync(
                    "详细",
                    "GitHub 报告",
                    $"用户取消文件错误报告。App ID={game.AppId}。",
                    detailed: true);
                return;
            }
            _reportDrafts[game.AppId] = draft;
            var issueFormUri = _reports.Prepare(draft);
            await _reports.OpenAsync(issueFormUri);
            await App.Logs.WriteAsync("信息", "GitHub 报告", "已打开预填的文件错误报告草稿。");
            await App.Logs.WriteAsync(
                "详细",
                "GitHub 报告",
                $"草稿已准备。App ID={draft.AppId}；游戏={draft.GameName}；错误类型={draft.ErrorType}。",
                detailed: true);
            App.ViewModel.ShowInfo(
                "GitHub 文件错误报告草稿已预填；请在浏览器中确认后提交。",
                InfoBarSeverity.Success);
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
            PrimaryButtonText = "打开网页草稿",
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
            errorType.SelectedItem?.ToString() ?? "文件可能过期",
            reason.Text,
            reference.Text);
    }
}
