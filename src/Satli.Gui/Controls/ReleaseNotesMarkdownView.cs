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
        catch
        {
            _webView.Visibility = Visibility.Collapsed;
            _fallback.Visibility = Visibility.Visible;
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
            _webView.NavigateToString(ReleaseNotesMarkdownFormatter.ToHtml(
                _markdown,
                _baseUri,
                ActualTheme == ElementTheme.Dark));
        }
        catch
        {
            // The readable plain-text fallback remains visible.
        }
    }

    private async void OnNavigationStarting(
        CoreWebView2 sender,
        CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.Uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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
            return;
        }

        _fallback.Visibility = Visibility.Collapsed;
        _webView.Visibility = Visibility.Visible;
    }
}
