using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Satli.Core.TranslationProjects;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.Pages;

public sealed partial class TranslationProjectsPage : Page
{
    private CancellationTokenSource? _operation;
    private readonly TextBox AppIdBox = new() { Header = "Steam App ID" };
    private readonly TextBox GameNameBox = new() { Header = "游戏名称（可选）" };
    private readonly TextBox VariantBox = new() { Header = "译本版本", Text = "default" };
    private readonly ComboBox TargetBox = new() { Header = "目标语言", DisplayMemberPath = "DisplayName", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox SourceLanguageBox = new() { Header = "原文语言（1–4 种，以逗号分隔）", Text = "english" };
    private readonly StackPanel CreationForm = new() { Spacing = 12 };
    private IReadOnlyList<TranslationProjectItem> _projects = [];

    public TranslationProjectsPage()
    {
        InitializeComponent();
        TargetBox.ItemsSource = SteamLanguageCatalog.CreateEditorOptions([]);
        TargetBox.SelectedIndex = 0;
        foreach (var control in new UIElement[] { AppIdBox, GameNameBox, VariantBox, TargetBox, SourceLanguageBox })
            CreationForm.Children.Add(control);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RefreshAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _operation?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var result = await Task.Run(() => TranslationProjectService.Store().LoadAllAsync());
            _projects = result.Projects.Select(project => new TranslationProjectItem(project)).ToArray();
            ApplySearch();
            if (result.Failed > 0)
            {
                StatusText.Text = $"有 {result.Failed} 个草稿无法读取，原文件已保留。";
                await App.Logs.WriteAsync("警告", "翻译制作", $"跳过无法读取的草稿：{result.Failed} 个。");
            }
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private string Target => (TargetBox.SelectedItem as SteamLanguageOption)?.Code ?? "schinese";

    private void Search_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplySearch();
    private void ApplySearch()
    {
        var query = SearchBox.Text.Trim();
        var items = _projects.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || item.Project.AppId.Contains(query, StringComparison.Ordinal)).ToArray();
        ProjectsList.ItemsSource = items;
        EmptyText.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<bool> ConfigureAsync(string title, bool needsAppId, bool publicSource = false)
    {
        if (_operation is not null) return false;
        AppIdBox.Visibility = VariantBox.Visibility = needsAppId ? Visibility.Visible : Visibility.Collapsed;
        SourceLanguageBox.Visibility = publicSource ? Visibility.Visible : Visibility.Collapsed;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new ScrollViewer { Content = CreationForm, MaxHeight = 470 },
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { ((ScrollViewer)dialog.Content).Content = null; dialog.Content = null; }
    }

    private TranslationProject NewPending()
    {
        var appId = AppIdBox.Text.Trim();
        var variant = VariantBox.Text.Trim();
        if (!uint.TryParse(appId, out var id) || id == 0 || id.ToString() != appId
            || !System.Text.RegularExpressions.Regex.IsMatch(variant, @"\A[a-z0-9][a-z0-9-]{0,63}\z"))
            throw new InvalidDataException("请输入有效的 App ID 和译本版本。");
        return new TranslationProject
        {
            AppId = appId,
            VariantId = variant,
            TargetLanguage = Target,
            GameName = string.IsNullOrWhiteSpace(GameNameBox.Text) ? $"Steam 游戏 {appId}" : GameNameBox.Text.Trim(),
        };
    }

    private async void Public_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfigureAsync("从 Steam 公开数据新建", true, true)) return;
        await RunAsync(async token =>
        {
            var pending = NewPending();
            var language = SourceLanguageBox.Text.Trim().ToLowerInvariant();
            pending.Source = $"Steam 公开页面 · {language}";
            await TranslationProjectService.Store().SaveAsync(pending);
            try
            {
                if (App.ViewModel.Settings.Offline) throw new InvalidOperationException("当前为离线模式，请导入 JSON 或关闭离线模式后重试。");
                using var client = NetworkHttpClientFactory.Create(App.ViewModel.Settings.Network);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var languages = language.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var source = await Task.Run(() => SteamPublicAchievements.FetchManyAsync(client, pending.AppId, languages, pending.VariantId, timeout.Token));
                var pendingId = pending.Id;
                pending = TranslationProject.Create(source.Translation,
                    string.IsNullOrWhiteSpace(GameNameBox.Text) ? source.GameName : pending.GameName, Target, source.Source);
                pending.Id = pendingId;
                pending.RetrievedAt = source.RetrievedAt;
                if (source.Warnings?.Count > 0)
                {
                    App.ViewModel.ShowInfo(string.Join("\n", source.Warnings), InfoBarSeverity.Warning);
                    await App.Logs.WriteAsync("警告", "翻译制作", $"公开来源有 {source.Warnings.Count} 种语言未合入。");
                }
                await OpenAsync(pending, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                pending.PreparationError = "公开数据获取失败，请补充 JSON 来源。";
                await TranslationProjectService.Store().SaveAsync(pending);
                throw new InvalidOperationException($"无法建立公开数据草稿：{exception.Message}；待补充来源的草稿已保存。", exception);
            }
        });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfigureAsync("导入 JSON 译文", false)) return;
        await RunAsync(async token =>
        {
            var path = PickJson();
            if (path is null) return;
            var project = await TranslationProjectService.ImportAsync(path, GameNameBox.Text.Trim(), Target);
            await OpenAsync(project, token);
        });
    }

    private async void Catalog_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfigureAsync("从在线译本创建草稿", true)) return;
        await RunAsync(async token =>
        {
            var pending = NewPending();
            var game = App.ViewModel.Translations.Games.FirstOrDefault(game => game.AppId == pending.AppId)
                ?? new GameItem { AppId = pending.AppId, GameName = pending.GameName };
            var downloaded = await new LiteWorkflowService().DownloadAsync(game, pending.VariantId, App.ViewModel.Settings);
            token.ThrowIfCancellationRequested();
            await OpenAsync(TranslationProject.Create(downloaded.Translation, game.GameName, Target,
                $"steam-achievement-translation-library · {pending.AppId}/{pending.VariantId}"), token);
        });
    }

    private async Task OpenAsync(TranslationProject project, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await TranslationProjectService.Store().SaveAsync(project);
        await App.Logs.WriteAsync("信息", "翻译制作", $"草稿已保存。App ID={project.AppId}；成就={project.Original?["achievements"]?.AsObject().Count ?? 0}。");
        Frame.Navigate(typeof(AchievementEditorPage), project);
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TranslationProjectItem item }) ProjectsList.SelectedItem = item;
        Resume();
    }
    private void Projects_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Resume();
    private void Resume()
    {
        if (_operation is not null || ProjectsList.SelectedItem is not TranslationProjectItem item) return;
        if (!item.Project.IsReady)
        {
            AppIdBox.Text = item.Project.AppId;
            GameNameBox.Text = item.Project.GameName;
            VariantBox.Text = item.Project.VariantId;
            TargetBox.SelectedItem = ((IEnumerable<SteamLanguageOption>)TargetBox.ItemsSource)
                .FirstOrDefault(option => option.Code == item.Project.TargetLanguage);
            StatusText.Text = "草稿尚无可信成就列表。可重新获取公开数据，或用 JSON 更新所选来源。";
            return;
        }
        Frame.Navigate(typeof(AchievementEditorPage), item.Project);
    }

    private async void UpdateSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TranslationProjectItem selected }) ProjectsList.SelectedItem = selected;
        if (ProjectsList.SelectedItem is not TranslationProjectItem item) return;
        await RunAsync(async token =>
        {
            var path = PickJson();
            if (path is null) return;
            var imported = await TranslationProjectService.ImportAsync(path, item.Project.GameName, item.Project.TargetLanguage);
            if (imported.AppId != item.Project.AppId || imported.VariantId != item.Project.VariantId)
                throw new InvalidDataException("JSON 的 App ID / Variant 与所选草稿不一致。");
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "确认来源更新",
                Content = item.Project.DescribeChanges(imported.Original!)
                    + "\n仅按完全相同的 API ID 保留人工译文，请重新校对原文变化的项目。旧草稿及已移除项目的修改会继续保留。",
                PrimaryButtonText = "另存并保留编辑",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await OpenAsync(item.Project.Rebase(imported.Original!, imported.Source), token);
        });
    }

    private static string? PickJson() => NativeFilePickerService.PickOpenFile(App.WindowHandle,
        "导入翻译 JSON", ("翻译 JSON", "*.json"));

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        foreach (var control in CreationForm.Children.OfType<Control>()) control.IsEnabled = false;
        Busy.Visibility = CancelButton.Visibility = Visibility.Visible;
        StatusText.Text = "正在准备译文来源…";
        await App.Logs.WriteAsync("信息", "翻译制作", "开始准备或更新草稿来源。");
        try { await action(cancellation.Token); }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消或超时。已保存的草稿保留，可重新获取或补充 JSON。";
            await App.Logs.WriteAsync("信息", "翻译制作", "来源准备已取消或超时。");
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            await App.Logs.WriteAsync("警告", "翻译制作", $"来源准备失败；异常类型={exception.GetType().Name}。");
        }
        finally
        {
            _operation = null;
            foreach (var control in CreationForm.Children.OfType<Control>()) control.IsEnabled = true;
            Busy.Visibility = CancelButton.Visibility = Visibility.Collapsed;
            await RefreshAsync();
        }
    }
}
