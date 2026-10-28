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
        void UpdateVariant()
        {
            if (variant.SelectedItem is not SchemaVariantOption option) return;
            method.ItemsSource = option.HasJson
                ? option.HasBin ? new[] { "JSON（lite）", "BIN" } : new[] { "JSON（lite）" }
                : new[] { "BIN" };
            method.SelectedIndex = 0;
            language.ItemsSource = option.Languages.Select(SteamLanguageCatalog.CreateOption).ToArray();
            language.DisplayMemberPath = nameof(SteamLanguageOption.DisplayName);
            language.SelectedIndex = Math.Max(0, option.Languages.ToList().IndexOf("schinese"));
        }
        variant.SelectionChanged += (_, _) => UpdateVariant();
        method.SelectionChanged += (_, _) =>
        {
            var bin = method.SelectedItem?.ToString() == "BIN";
            language.Visibility = bin ? Visibility.Collapsed : Visibility.Visible;
            description.Text = bin ? "备份并写入本机成就文件。" : "使用 SATLI-lite 在 Steam 中显示译文。";
            dialog.PrimaryButtonText = bin ? "安装 BIN" : "使用 lite";
        };
        UpdateVariant();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return null;
        if (variant.SelectedItem is not SchemaVariantOption chosen) return null;
        game.SelectedVariant = chosen;
        return new(method.SelectedItem?.ToString() == "BIN" ? "bin" : "json",
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
