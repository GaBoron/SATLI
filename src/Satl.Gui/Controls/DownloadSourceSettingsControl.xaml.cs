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

    private void ResetIndexOrder_Click(object sender, RoutedEventArgs e)
    {
        Replace(
            IndexSources,
            DownloadSourceCatalog.Options(DownloadSourceDefaults.IndexOrder));
        NotifyChanged();
    }

    private void ResetFileOrder_Click(object sender, RoutedEventArgs e)
    {
        Replace(
            FileSources,
            DownloadSourceCatalog.Options(DownloadSourceDefaults.FileOrder));
        NotifyChanged();
    }

    private void SourceList_DragItemsCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args) => NotifyChanged();

    private void NotifyChanged()
    {
        if (!_isInitializing)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
