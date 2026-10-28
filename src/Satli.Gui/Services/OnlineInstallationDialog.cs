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
        var language = new ComboBox { Header = "JSON 目标语言", HorizontalAlignment = HorizontalAlignment.Stretch };
        var availability = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 12, MinWidth = 420, MaxWidth = 560 };
        content.Children.Add(new TextBlock { Text = game.GameName, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(variant);
        content.Children.Add(availability);
        content.Children.Add(method);
        content.Children.Add(language);
        content.Children.Add(new TextBlock { Text = "JSON 首次即可通过 SATLI-lite 应用，无需先安装 BIN。下载和预览 JSON 无需插件；传统 BIN 安装独立可用。", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { XamlRoot = root, Title = "选择译本安装方式", Content = content,
            PrimaryButtonText = "继续", SecondaryButtonText = "仅下载和预览 JSON", CloseButtonText = "取消" };
        void UpdateVariant()
        {
            if (variant.SelectedItem is not SchemaVariantOption option) return;
            method.ItemsSource = option.HasJson
                ? option.HasBin ? new[] { "通过 SATLI-lite 应用 JSON", "安装本地 BIN" } : new[] { "通过 SATLI-lite 应用 JSON" }
                : new[] { "安装本地 BIN" };
            method.SelectedIndex = 0;
            language.ItemsSource = option.Languages.Select(SteamLanguageCatalog.CreateOption).ToArray();
            language.DisplayMemberPath = nameof(SteamLanguageOption.DisplayName);
            language.SelectedIndex = Math.Max(0, option.Languages.ToList().IndexOf("schinese"));
            availability.Text = option.AvailableMethods;
            dialog.IsSecondaryButtonEnabled = option.HasJson;
        }
        variant.SelectionChanged += (_, _) => UpdateVariant();
        method.SelectionChanged += (_, _) => language.IsEnabled = method.SelectedItem?.ToString() != "安装本地 BIN";
        UpdateVariant();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return null;
        if (variant.SelectedItem is not SchemaVariantOption chosen) return null;
        game.SelectedVariant = chosen;
        return new(result == ContentDialogResult.Secondary ? "download"
            : method.SelectedItem?.ToString() == "安装本地 BIN" ? "bin" : "json",
            chosen.VariantId, (language.SelectedItem as SteamLanguageOption)?.Code ?? "");
    }

    public static async Task RunAsync(XamlRoot root, GameItem game)
    {
        try
        {
            var choice = await ChooseAsync(root, game);
            if (choice is null) return;
            var model = App.ViewModel.Translations;
            if (choice.Method == "download") await model.DownloadJsonAsync(root, game, choice.VariantId);
            else if (choice.Method == "json") await model.ApplyLiteAsync(root, game, choice.VariantId, choice.Language);
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
