using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public static class SchemaRevisionDiffDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot, SchemaRevisionDiff diff, string title)
    {
        var languageBox = new ComboBox
        {
            Header = "显示语言",
            MinWidth = 220,
            ItemsSource = diff.Languages,
            SelectedItem = diff.DefaultLanguage,
        };
        var summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
        };
        var tableHost = new Grid();

        void Render()
        {
            var language = languageBox.SelectedItem as string ?? diff.DefaultLanguage;
            var lines = diff.LinesFor(language);
            var removed = lines.Count(line => line.Kind == RevisionDiffLineKind.Removed);
            var added = lines.Count - removed;
            summary.Text = diff.HasParent
                ? $"相对上一个 Git 修订：删除 {removed} 行，新增 {added} 行。"
                : $"这是首个 Git 修订：全部内容视为新增，共 {added} 行。";
            tableHost.Children.Clear();
            tableHost.Children.Add(BuildTable(lines));
        }

        languageBox.SelectionChanged += (_, _) => Render();
        var content = new Grid
        {
            RowSpacing = 10,
            Width = Math.Clamp(xamlRoot.Size.Width - 96, 520, 1120),
            Height = Math.Clamp(xamlRoot.Size.Height - 240, 260, 620),
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(summary);
        Grid.SetRow(languageBox, 1);
        content.Children.Add(languageBox);
        Grid.SetRow(tableHost, 2);
        content.Children.Add(tableHost);
        Render();

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = content.Width + 48;
        dialog.Resources["ContentDialogMinWidth"] = Math.Min(640, content.Width + 48);
        await dialog.ShowAsync();
    }

    private static UIElement BuildTable(IReadOnlyList<RevisionDiffLine> lines)
    {
        if (lines.Count == 0)
        {
            return new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Informational,
                Title = "所选语言没有变化",
                Message = "此修订可能修改了其他语言，或内容与父修订一致。",
            };
        }

        var rows = new StackPanel { Spacing = 1 };
        foreach (var line in lines)
        {
            rows.Children.Add(BuildLine(line));
        }
        return new ScrollViewer
        {
            Content = rows,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
        };
    }

    private static Grid BuildLine(RevisionDiffLine line)
    {
        var removed = line.Kind == RevisionDiffLineKind.Removed;
        var foreground = Resource<Brush>(removed
            ? "SystemFillColorCriticalBrush"
            : "SystemFillColorSuccessBrush");
        var grid = new Grid
        {
            Padding = new Thickness(8, 7, 8, 7),
            ColumnSpacing = 8,
            Background = Resource<Brush>(removed
                ? "SystemFillColorCriticalBackgroundBrush"
                : "SystemFillColorSuccessBackgroundBrush"),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var values = new[]
        {
            line.Prefix,
            line.Field,
            $"#{line.Index} · {line.ApiName}",
            line.Text,
        };
        for (var column = 0; column < values.Length; column++)
        {
            var text = new TextBlock
            {
                Text = values[column],
                TextWrapping = TextWrapping.Wrap,
                Foreground = foreground,
                FontWeight = column == 0 ? FontWeights.Bold : FontWeights.Normal,
            };
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
        return grid;
    }

    private static T? Resource<T>(string key) where T : class =>
        Application.Current.Resources[key] as T;
}
