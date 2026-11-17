using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

internal sealed record OnlineInstallationChoice(string Method, string VariantId, string Language);

internal static class OnlineInstallationDialog
{
    public static async Task<OnlineInstallationChoice?> ChooseAsync(XamlRoot root, GameItem game)
    {
        if (game.Variants.Count == 0) throw new InvalidOperationException("此游戏没有在线译本");
        var variant = new ComboBox { Header = "译本版本", ItemsSource = game.Variants, SelectedItem = game.SelectedVariant };
        var method = new ComboBox { Header = "安装方式", HorizontalAlignment = HorizontalAlignment.Stretch };
        var language = new ComboBox { Header = "语言", HorizontalAlignment = HorizontalAlignment.Stretch };
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 12, MinWidth = 420, MaxWidth = 560 };
        content.Children.Add(new TextBlock { Text = game.GameName, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(variant);
        content.Children.Add(method);
        content.Children.Add(language);
        content.Children.Add(description);
        var dialog = new ContentDialog { XamlRoot = root, Title = "安装翻译", Content = content,
            PrimaryButtonText = "使用 lite", CloseButtonText = "取消" };
        bool IsBin() => (method.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "bin";
        void UpdateMethod()
        {
            var selected = method.SelectedItem as ComboBoxItem;
            var bin = IsBin();
            language.Visibility = bin || selected is null ? Visibility.Collapsed : Visibility.Visible;
            description.Text = selected is null ? LegacyBinInstallation.LockedMessage
                : bin ? "旧方式：备份并写入本机成就文件，可能不生效或被 Steam 覆盖。"
                : "使用 SATLI-lite 在 Steam 中显示译文；缺少 Millennium 时由 SATLI 一并下载安装。";
            dialog.PrimaryButtonText = bin ? "安装 BIN" : "使用 lite";
            dialog.IsPrimaryButtonEnabled = selected?.IsEnabled == true;
        }
        void UpdateVariant()
        {
            if (variant.SelectedItem is not SchemaVariantOption option) return;
            var unlocked = App.ViewModel.Settings.UseLegacyBinInstallation;
            method.Items.Clear();
            method.Items.Add(new ComboBoxItem { Content = "JSON（lite）", Tag = "json", IsEnabled = option.HasJson });
            var binItem = new ComboBoxItem
            {
                Content = !option.HasBin ? "BIN（此译本未提供）" : unlocked ? "BIN（旧方式）" : "BIN（已锁定）",
                Tag = "bin", IsEnabled = option.HasBin && unlocked,
            };
            ToolTipService.SetToolTip(binItem, !unlocked ? LegacyBinInstallation.LockedMessage
                : "旧方式会写入 Steam 成就缓存，可能不生效或被覆盖。");
            method.Items.Add(binItem);
            method.SelectedIndex = option.HasJson ? 0 : option.HasBin && unlocked ? 1 : -1;
            method.PlaceholderText = "BIN 已锁定，请在设置中解锁";
            language.ItemsSource = option.Languages.Select(SteamLanguageCatalog.CreateOption).ToArray();
            language.DisplayMemberPath = nameof(SteamLanguageOption.DisplayName);
            language.SelectedIndex = Math.Max(0, option.Languages.ToList().IndexOf("schinese"));
            UpdateMethod();
        }
        variant.SelectionChanged += (_, _) => UpdateVariant();
        method.SelectionChanged += (_, _) => UpdateMethod();
        UpdateVariant();
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || method.SelectedItem is not ComboBoxItem { IsEnabled: true }) return null;
        if (variant.SelectedItem is not SchemaVariantOption chosen) return null;
        game.SelectedVariant = chosen;
        return new(IsBin() ? "bin" : "json",
            chosen.VariantId, (language.SelectedItem as SteamLanguageOption)?.Code ?? "");
    }

    public static async Task RunAsync(XamlRoot root, GameItem game)
    {
        try
        {
            var choice = await ChooseAsync(root, game);
            if (choice is null) return;
            var model = App.ViewModel.Translations;
            if (choice.Method == "json") await model.ApplyLiteAsync(root, game, choice.VariantId, choice.Language);
            else
            {
                var previews = await model.PreviewInstallAsync([game]);
                if (previews is not null && await ReplacementConfirmationDialog.ShowAsync(root, previews, "确认安装本地 BIN", "安装 BIN"))
                    await new SteamMutationDialogService().ExecuteAsync(root, () => model.InstallAsync([game]));
            }
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"安装未完成：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("在线译本安装", exception);
        }
    }
}
