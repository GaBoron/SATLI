using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.Controls;

public sealed partial class DownloadSourceSettingsControl : UserControl
{
    private bool _isInitializing;

    public event EventHandler? SettingsChanged;

    public ObservableCollection<DownloadSourceOption> IndexSources { get; } = [];
    public ObservableCollection<DownloadSourceOption> FileSources { get; } = [];

    public DownloadSourceSettingsControl()
    {
        InitializeComponent();
    }

    public void LoadSettings(DownloadSourceSettings? settings)
    {
        _isInitializing = true;
        var normalized = DownloadSourceCatalog.Normalize(settings);
        Replace(IndexSources, DownloadSourceCatalog.Options(normalized.IndexSourceOrder));
        Replace(FileSources, DownloadSourceCatalog.Options(normalized.FileSourceOrder));
        _isInitializing = false;
    }

    public DownloadSourceSettings ReadSettings() => new()
    {
        IndexSourceOrder = IndexSources.Select(source => source.Id).ToList(),
        FileSourceOrder = FileSources.Select(source => source.Id).ToList(),
    };

    private void IndexMoveUp_Click(object sender, RoutedEventArgs e) =>
        Move(IndexSources, SourceId(sender), -1);

    private void IndexMoveDown_Click(object sender, RoutedEventArgs e) =>
        Move(IndexSources, SourceId(sender), 1);

    private void FileMoveUp_Click(object sender, RoutedEventArgs e) =>
        Move(FileSources, SourceId(sender), -1);

    private void FileMoveDown_Click(object sender, RoutedEventArgs e) =>
        Move(FileSources, SourceId(sender), 1);

    private void ResetOrder_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings(new DownloadSourceSettings());
        NotifyChanged();
    }

    private void Move(
        ObservableCollection<DownloadSourceOption> sources,
        string sourceId,
        int offset)
    {
        var source = sources.FirstOrDefault(item => item.Id == sourceId);
        if (source is null)
        {
            return;
        }
        var index = sources.IndexOf(source);
        var destination = index + offset;
        if (destination < 0 || destination >= sources.Count)
        {
            return;
        }
        sources.Move(index, destination);
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (!_isInitializing)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string SourceId(object sender) =>
        (sender as Button)?.Tag?.ToString() ?? string.Empty;

    private static void Replace(
        ObservableCollection<DownloadSourceOption> destination,
        IEnumerable<DownloadSourceOption> sources)
    {
        destination.Clear();
        foreach (var source in sources)
        {
            destination.Add(source);
        }
    }
}
