using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Satli_Gui.Services;

namespace Satli_Gui.Controls;

public sealed partial class LoadingStateControl : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(LoadingStateControl),
        new PropertyMetadata(false, OnIsActiveChanged));

    private LoadingTipService? _loadingTips;
    private bool _isInitialized;
    private bool _wasActive;
    private int _tipRequestId;

    public LoadingStateControl()
    {
        InitializeComponent();
        _isInitialized = true;
        Visibility = Visibility.Collapsed;
        Loaded += LoadingStateControl_Loaded;
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static void OnIsActiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((LoadingStateControl)sender).UpdateState();

    private void LoadingStateControl_Loaded(object sender, RoutedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        if (!_isInitialized)
        {
            return;
        }

        Visibility = IsActive ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = IsActive;
        if (IsActive && !_wasActive)
        {
            _ = RefreshTipAsync();
        }
        else if (!IsActive)
        {
            _tipRequestId++;
        }
        _wasActive = IsActive;
    }

    private async Task RefreshTipAsync()
    {
        var requestId = ++_tipRequestId;
        try
        {
            _loadingTips = new LoadingTipService(App.ViewModel.CurrentDataDirectory);
            var tip = await _loadingTips.GetTipAsync();
            if (requestId == _tipRequestId && IsActive)
            {
                TipText.Text = $"Tip：{tip}";
            }
        }
        catch (Exception exception)
        {
            TipText.Text = "Tip：成就们正在排队点名……";
            await App.Logs.WriteExceptionDetailsAsync("加载提示", exception);
        }
    }

    private async void TipText_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _loadingTips ??= new LoadingTipService(App.ViewModel.CurrentDataDirectory);
        if (!await _loadingTips.OpenForEditingAsync())
        {
            App.ViewModel.ShowInfo("无法打开加载提示文件。", InfoBarSeverity.Warning);
        }
        e.Handled = true;
    }
}
