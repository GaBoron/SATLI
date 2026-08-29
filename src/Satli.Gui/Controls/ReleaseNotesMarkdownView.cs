using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Satli_Gui.Services;
using Windows.System;

namespace Satli_Gui.Controls;

public sealed class ReleaseNotesMarkdownView : Grid
{
    private readonly string _markdown;
    private readonly Uri _baseUri;
    private readonly TextBlock _fallback;
    private readonly WebView2 _webView;
    private bool _initialized;
    private bool _awaitingDocumentNavigation;

    public ReleaseNotesMarkdownView(string markdown, Uri baseUri)
    {
        _markdown = string.IsNullOrWhiteSpace(markdown)
            ? "此版本未提供发布说明。"
            : markdown;
        _baseUri = baseUri;

        _fallback = new TextBlock
        {
            Text = ReleaseNotesMarkdownFormatter.ToPlainText(_markdown),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        _webView = new WebView2
        {
            Visibility = Visibility.Collapsed,
        };

        Children.Add(_fallback);
        Children.Add(_webView);
        Loaded += OnLoaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await App.Logs.WriteAsync(
            "调试",
            "更新说明",
            "开始初始化 Markdown 更新说明渲染器。",
            debug: true);
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty,
                ApplicationDataPaths.WebViewUserDataDirectory,
                new CoreWebView2EnvironmentOptions());
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsScriptEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _webView.NavigationCompleted += OnNavigationCompleted;
            RenderOrShowFallback();
        }
        catch (Exception exception)
        {
            _webView.Visibility = Visibility.Collapsed;
            _fallback.Visibility = Visibility.Visible;
            await App.Logs.WriteAsync(
                "警告",
                "更新说明",
                "Markdown 渲染组件初始化失败，已使用可读的纯文本更新说明。");
            await App.Logs.WriteExceptionDetailsAsync("更新说明", exception);
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_webView.CoreWebView2 is not null)
        {
            RenderOrShowFallback();
        }
    }

    private void RenderOrShowFallback()
    {
        _fallback.Visibility = Visibility.Visible;
        _webView.Visibility = Visibility.Collapsed;
        try
        {
            _awaitingDocumentNavigation = true;
            _webView.NavigateToString(ReleaseNotesMarkdownFormatter.ToHtml(
                _markdown,
                _baseUri,
                ActualTheme == ElementTheme.Dark));
        }
        catch (Exception exception)
        {
            _awaitingDocumentNavigation = false;
            // The readable plain-text fallback remains visible.
            _ = App.Logs.WriteAsync(
                "警告",
                "更新说明",
                "Markdown 更新说明导航失败，已使用可读的纯文本内容。");
            _ = App.Logs.WriteExceptionDetailsAsync("更新说明", exception);
        }
    }

    private async void OnNavigationStarting(
        CoreWebView2 sender,
        CoreWebView2NavigationStartingEventArgs args)
    {
        if (_awaitingDocumentNavigation
            && (args.Uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase)
                || args.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)))
        {
            _awaitingDocumentNavigation = false;
            return;
        }

        _awaitingDocumentNavigation = false;
        args.Cancel = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto")
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            _fallback.Visibility = Visibility.Visible;
            _webView.Visibility = Visibility.Collapsed;
            _ = App.Logs.WriteAsync(
                "警告",
                "更新说明",
                $"Markdown 更新说明渲染失败，已使用纯文本内容。状态={args.WebErrorStatus}。");
            return;
        }

        _fallback.Visibility = Visibility.Collapsed;
        _webView.Visibility = Visibility.Visible;
        _ = App.Logs.WriteAsync(
            "详细",
            "更新说明",
            "Markdown 更新说明渲染完成。",
            detailed: true);
    }
}
