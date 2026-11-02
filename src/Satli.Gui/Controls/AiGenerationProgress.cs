using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.TranslationProjects;

namespace Satli_Gui.Controls;

/// <summary>Throttles streaming UI updates. Provider summaries are displayed only for this dialog's lifetime.</summary>
internal sealed class AiGenerationProgress : StackPanel, IDisposable
{
    private readonly object _gate = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly TextBlock _stageText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _countsText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _clockText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summaryText = new()
    {
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.Wrap,
        Text = "等待服务返回思考文本或摘要；部分接口或模型不提供。",
    };
    private readonly ScrollViewer _summaryScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(0, 8, 12, 8),
    };
    private readonly CheckBox _followLatest = new() { Content = "跟随最新内容", IsChecked = true, MinWidth = 0 };
    private readonly StringBuilder _summary = new();
    private string _stage = "正在连接服务";
    private string _counts;
    private long _lastEvent;
    private int _characters;
    private bool _summaryChanged;

    public AiGenerationProgress(int total, double availableHeight)
    {
        Spacing = 12;
        _counts = $"待生成 {total} 项。";
        Children.Add(new ProgressBar { IsIndeterminate = true });
        Children.Add(_stageText);
        Children.Add(_countsText);
        Children.Add(_clockText);
        var summaryHeader = new Grid();
        summaryHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summaryHeader.Children.Add(new TextBlock { Text = "服务提供的思考过程 / 摘要", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_followLatest, 1);
        summaryHeader.Children.Add(_followLatest);
        Children.Add(summaryHeader);
        _summaryScroll.Content = _summaryText;
        _summaryScroll.Height = Math.Clamp(availableHeight - 360, 180, 440);
        Children.Add(_summaryScroll);
        _summaryText.SizeChanged += Summary_SizeChanged;
        _followLatest.Checked += FollowLatest_Checked;
        _timer.Tick += Tick;
        Tick(null, null);
        _timer.Start();
    }

    private void Summary_SizeChanged(object sender, SizeChangedEventArgs args) => ScrollToLatest();
    private void FollowLatest_Checked(object sender, RoutedEventArgs args) => ScrollToLatest();
    private void ScrollToLatest()
    {
        if (_followLatest.IsChecked != true) return;
        // Wait until the new text has been measured and the scroll extent is current.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_followLatest.IsChecked == true) _summaryScroll.ChangeView(null, _summaryScroll.ScrollableHeight, null, true);
        });
    }

    public void Activity(AiStreamActivity activity)
    {
        lock (_gate)
        {
            _stage = activity.Stage;
            _lastEvent = _elapsed.ElapsedMilliseconds;
            _characters = activity.ReceivedCharacters;
            if (activity.SummaryDelta.Length == 0) return;
            _summary.Append(activity.SummaryDelta);
            if (_summary.Length > 8000) _summary.Remove(0, _summary.Length - 8000);
            _summaryChanged = true;
        }
    }

    public void Batch(AiTranslationProgress progress)
    {
        lock (_gate)
        {
            _counts = $"已校验 {progress.Completed}/{progress.Total} 项；失败 {progress.Failed} 项。";
            _stage = "批次已完成并保存";
            _lastEvent = _elapsed.ElapsedMilliseconds;
            if (_summary.Length > 0) { _summary.AppendLine().AppendLine(); _summaryChanged = true; }
        }
    }

    private void Tick(object? sender, object? args)
    {
        lock (_gate)
        {
            _stageText.Text = _stage;
            _countsText.Text = _counts;
            var quiet = Math.Max(0, (_elapsed.ElapsedMilliseconds - _lastEvent) / 1000);
            _clockText.Text = $"已用时 {_elapsed.Elapsed:mm\\:ss} · 距上次响应 {quiet} 秒"
                + (_characters > 0 ? $" · 本批已接收 {_characters} 字符" : "");
            if (!_summaryChanged) return;
            _summaryText.Text = _summary.ToString();
            _summaryChanged = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Tick;
        _summaryText.SizeChanged -= Summary_SizeChanged;
        _followLatest.Checked -= FollowLatest_Checked;
        _elapsed.Stop();
    }
}
