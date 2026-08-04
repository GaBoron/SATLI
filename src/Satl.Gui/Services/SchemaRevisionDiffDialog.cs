using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public static class SchemaRevisionDiffDialog
{
    private const double DialogMargin = 48;
    private const double DialogChromeHeight = 170;

    public static async Task ShowAsync(XamlRoot xamlRoot, SchemaRevisionDiff diff, string title)
    {
        await ShowCoreAsync(xamlRoot, diff, title, "上一个 Git 修订", confirmText: null);
    }

    public static Task<bool> ConfirmAsync(
        XamlRoot xamlRoot,
        SchemaRevisionDiff diff,
        string title,
        string comparisonBaseline,
        string confirmText) =>
        ShowCoreAsync(xamlRoot, diff, title, comparisonBaseline, confirmText);

    private static async Task<bool> ShowCoreAsync(
        XamlRoot xamlRoot,
        SchemaRevisionDiff diff,
        string title,
        string comparisonBaseline,
        string? confirmText)
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
        var tableHost = new Grid { MinHeight = 180 };

        void Render()
        {
            var language = languageBox.SelectedItem as string ?? diff.DefaultLanguage;
            var rows = diff.RowsFor(language);
            var values = rows.SelectMany(row => new[] { row.Name, row.Description }).ToArray();
            var removed = values.Count(value =>
                value.Kind is RevisionDiffKind.Removed or RevisionDiffKind.Modified);
            var added = values.Count(value =>
                value.Kind is RevisionDiffKind.Added or RevisionDiffKind.Modified);
            summary.Text = diff.HasParent
                ? $"完整显示 {rows.Count} 项成就；相对{comparisonBaseline}，删除 {removed} 行，新增 {added} 行。"
                : $"没有{comparisonBaseline}可比较；完整显示 {rows.Count} 项成就。";
            tableHost.Children.Clear();
            tableHost.Children.Add(BuildTable(rows));
        }

        languageBox.SelectionChanged += (_, _) => Render();
        var content = new Grid { RowSpacing = 10 };
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
            CloseButtonText = confirmText is null ? "关闭" : "取消",
            DefaultButton = confirmText is null
                ? ContentDialogButton.Close
                : ContentDialogButton.Primary,
        };
        if (confirmText is not null)
        {
            dialog.PrimaryButtonText = confirmText;
        }

        void ApplyAdaptiveSize()
        {
            var dialogWidth = Math.Clamp(xamlRoot.Size.Width - DialogMargin, 620, 1280);
            var dialogHeight = Math.Clamp(xamlRoot.Size.Height - DialogMargin, 480, 840);
            content.Width = dialogWidth - DialogMargin;
            content.Height = dialogHeight - DialogChromeHeight;
            dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
            dialog.Resources["ContentDialogMinWidth"] = Math.Min(720, dialogWidth);
            dialog.Resources["ContentDialogMaxHeight"] = dialogHeight;
        }

        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyAdaptiveSize();
        ApplyAdaptiveSize();
        xamlRoot.Changed += RootChanged;
        try
        {
            var result = await dialog.ShowAsync();
            return confirmText is null || result == ContentDialogResult.Primary;
        }
        finally
        {
            xamlRoot.Changed -= RootChanged;
        }
    }

    private static UIElement BuildTable(IReadOnlyList<SchemaRevisionDiffRow> rows)
    {
        if (rows.Count == 0)
        {
            return new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Informational,
                Title = "没有成就内容",
                Message = "此修订中没有识别到成就记录。",
            };
        }

        var table = new Grid { RowSpacing = 1 };
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        table.Children.Add(BuildHeader());
        var body = new StackPanel { Spacing = 1 };
        foreach (var row in rows)
        {
            body.Children.Add(BuildRow(row));
        }
        var scrollViewer = new ScrollViewer
        {
            Content = body,
            Padding = new Thickness(0, 0, 8, 8),
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
        };
        Grid.SetRow(scrollViewer, 1);
        table.Children.Add(scrollViewer);
        return table;
    }

    private static Grid BuildHeader()
    {
        var grid = CreateGrid();
        grid.Background = Resource<Brush>("SubtleFillColorSecondaryBrush");
        AddText(grid, 0, "#", FontWeights.SemiBold);
        AddText(grid, 1, "成就 ID", FontWeights.SemiBold);
        AddText(grid, 2, "名称", FontWeights.SemiBold);
        AddText(grid, 3, "说明", FontWeights.SemiBold);
        return grid;
    }

    private static Grid BuildRow(SchemaRevisionDiffRow row)
    {
        var grid = CreateGrid();
        AddText(grid, 0, row.Index.ToString(), FontWeights.Normal);
        grid.Children.Add(BuildIdentity(row));
        var name = BuildValue(row.Name);
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        var description = BuildValue(row.Description);
        Grid.SetColumn(description, 3);
        grid.Children.Add(description);
        return grid;
    }

    private static Grid CreateGrid()
    {
        var grid = new Grid
        {
            Padding = new Thickness(8, 7, 8, 7),
            ColumnSpacing = 8,
            Background = Resource<Brush>("LayerFillColorDefaultBrush"),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        return grid;
    }

    private static UIElement BuildIdentity(SchemaRevisionDiffRow row)
    {
        var prefix = row.RowKind switch
        {
            RevisionDiffKind.Added => "+ ",
            RevisionDiffKind.Removed => "− ",
            _ => string.Empty,
        };
        var content = new TextBlock { Text = prefix + row.ApiName, TextWrapping = TextWrapping.Wrap };
        FrameworkElement element = row.RowKind switch
        {
            RevisionDiffKind.Added => DiffBorder(content, removed: false),
            RevisionDiffKind.Removed => DiffBorder(content, removed: true),
            _ => content,
        };
        Grid.SetColumn(element, 1);
        return element;
    }

    private static FrameworkElement BuildValue(RevisionDiffValue value)
    {
        if (value.Kind == RevisionDiffKind.Unchanged)
        {
            return new TextBlock { Text = value.Current, TextWrapping = TextWrapping.Wrap };
        }
        var panel = new StackPanel { Spacing = 1 };
        if (value.Kind is RevisionDiffKind.Removed or RevisionDiffKind.Modified)
        {
            panel.Children.Add(DiffBorder(
                new TextBlock { Text = "− " + value.Previous, TextWrapping = TextWrapping.Wrap },
                removed: true));
        }
        if (value.Kind is RevisionDiffKind.Added or RevisionDiffKind.Modified)
        {
            panel.Children.Add(DiffBorder(
                new TextBlock { Text = "+ " + value.Current, TextWrapping = TextWrapping.Wrap },
                removed: false));
        }
        return panel;
    }

    private static Border DiffBorder(TextBlock text, bool removed)
    {
        text.Foreground = Resource<Brush>(removed
            ? "SystemFillColorCriticalBrush"
            : "SystemFillColorSuccessBrush");
        return new Border
        {
            Padding = new Thickness(4, 2, 4, 2),
            Background = Resource<Brush>(removed
                ? "SystemFillColorCriticalBackgroundBrush"
                : "SystemFillColorSuccessBackgroundBrush"),
            Child = text,
        };
    }

    private static void AddText(
        Grid grid,
        int column,
        string value,
        Windows.UI.Text.FontWeight fontWeight)
    {
        var text = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = fontWeight,
        };
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }

    private static T? Resource<T>(string key) where T : class =>
        Application.Current.Resources[key] as T;
}
