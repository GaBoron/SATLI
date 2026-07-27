using System.Collections.ObjectModel;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Windows.System;

namespace Satl_Gui.Controls;

public sealed partial class DownloadSourceSettingsControl : UserControl
{
    private bool _isInitializing;
    private ListView? _dragList;
    private DownloadSourceOption? _dragSource;
    private double _dragOffset;
    private int _dragOriginalIndex = -1;
    private int _dragTargetIndex = -1;

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

    private void SourceThumb_DragStarted(object sender, DragStartedEventArgs args)
    {
        if (sender is not FrameworkElement thumb
            || thumb.DataContext is not DownloadSourceOption source
            || FindAncestor<ListView>(thumb) is not { } list)
        {
            return;
        }

        _dragList = list;
        _dragSource = source;
        _dragOffset = 0;
        _dragOriginalIndex = SourcesFor(list).IndexOf(source);
        _dragTargetIndex = _dragOriginalIndex;
        UpdateDragVisuals();
    }

    private void SourceThumb_DragDelta(object sender, DragDeltaEventArgs args)
    {
        if (_dragList is null || _dragSource is null)
        {
            return;
        }

        _dragOffset += args.VerticalChange;
        var sources = SourcesFor(_dragList);
        if (_dragOriginalIndex < 0 || _dragOriginalIndex >= sources.Count)
        {
            return;
        }

        _dragOffset = Math.Clamp(
            _dragOffset,
            -DistanceToEdge(_dragList, 0, _dragOriginalIndex),
            DistanceToEdge(_dragList, _dragOriginalIndex + 1, sources.Count));
        _dragTargetIndex = FindTargetIndex();
        UpdateDragVisuals();
    }

    private void SourceThumb_DragCompleted(object sender, DragCompletedEventArgs args)
    {
        var list = _dragList;
        var source = _dragSource;
        var oldIndex = _dragOriginalIndex;
        var newIndex = _dragTargetIndex;
        ClearDragVisuals();
        _dragList = null;
        _dragSource = null;
        _dragOffset = 0;
        _dragOriginalIndex = -1;
        _dragTargetIndex = -1;
        if (list is not null
            && source is not null
            && oldIndex >= 0
            && newIndex >= 0
            && oldIndex != newIndex)
        {
            SourcesFor(list).Move(oldIndex, newIndex);
            list.ScrollIntoView(source);
            NotifyChanged();
        }
    }

    private void SourceThumb_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is not FrameworkElement thumb
            || thumb.DataContext is not DownloadSourceOption source
            || FindAncestor<ListView>(thumb) is not { } list
            || args.Key is not (VirtualKey.Up or VirtualKey.Down))
        {
            return;
        }

        var sources = SourcesFor(list);
        var oldIndex = sources.IndexOf(source);
        var newIndex = args.Key == VirtualKey.Up ? oldIndex - 1 : oldIndex + 1;
        if (oldIndex >= 0 && newIndex >= 0 && newIndex < sources.Count)
        {
            sources.Move(oldIndex, newIndex);
            NotifyChanged();
        }

        args.Handled = true;
    }

    private ObservableCollection<DownloadSourceOption> SourcesFor(ListView list) =>
        ReferenceEquals(list, IndexSourceList) ? IndexSources : FileSources;

    private static double ItemHeight(ListView list, int index) =>
        list.ContainerFromIndex(index) is FrameworkElement container
            ? Math.Max(container.ActualHeight, 1)
            : 56;

    private static double DistanceToEdge(ListView list, int start, int end)
    {
        var distance = 0d;
        for (var index = start; index < end; index++)
        {
            distance += ItemHeight(list, index);
        }

        return distance;
    }

    private int FindTargetIndex()
    {
        if (_dragList is null || _dragOriginalIndex < 0)
        {
            return -1;
        }

        var target = _dragOriginalIndex;
        var travelled = 0d;
        if (_dragOffset >= 0)
        {
            for (var index = _dragOriginalIndex + 1; index < _dragList.Items.Count; index++)
            {
                var height = ItemHeight(_dragList, index);
                if (_dragOffset < travelled + height / 2)
                {
                    break;
                }

                target = index;
                travelled += height;
            }
        }
        else
        {
            for (var index = _dragOriginalIndex - 1; index >= 0; index--)
            {
                var height = ItemHeight(_dragList, index);
                if (-_dragOffset < travelled + height / 2)
                {
                    break;
                }

                target = index;
                travelled += height;
            }
        }

        return target;
    }

    private void UpdateDragVisuals()
    {
        if (_dragList is null || _dragOriginalIndex < 0)
        {
            return;
        }

        var draggedHeight = ItemHeight(_dragList, _dragOriginalIndex);
        for (var index = 0; index < _dragList.Items.Count; index++)
        {
            if (_dragList.ContainerFromIndex(index) is not ListViewItem item)
            {
                continue;
            }

            if (index == _dragOriginalIndex)
            {
                item.TranslationTransition = null;
                item.Translation = new Vector3(0, (float)_dragOffset, 16);
                item.Scale = new Vector3(1.01f, 1.01f, 1);
                item.Opacity = 0.88;
                Canvas.SetZIndex(item, 1);
                continue;
            }

            item.TranslationTransition ??= new Vector3Transition
            {
                Duration = TimeSpan.FromMilliseconds(120),
            };
            var shift = 0f;
            if (_dragTargetIndex > _dragOriginalIndex
                && index > _dragOriginalIndex
                && index <= _dragTargetIndex)
            {
                shift = (float)-draggedHeight;
            }
            else if (_dragTargetIndex < _dragOriginalIndex
                     && index >= _dragTargetIndex
                     && index < _dragOriginalIndex)
            {
                shift = (float)draggedHeight;
            }

            item.Translation = new Vector3(0, shift, 0);
        }
    }

    private void ClearDragVisuals()
    {
        if (_dragList is null)
        {
            return;
        }

        for (var index = 0; index < _dragList.Items.Count; index++)
        {
            if (_dragList.ContainerFromIndex(index) is not ListViewItem item)
            {
                continue;
            }

            item.TranslationTransition = null;
            item.Translation = Vector3.Zero;
            item.Scale = Vector3.One;
            item.Opacity = 1;
            Canvas.SetZIndex(item, 0);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

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
