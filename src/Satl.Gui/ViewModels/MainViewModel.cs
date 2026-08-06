using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly UpdateService _updateService = new();
    private readonly StoreUpdateService _storeUpdateService;
    private readonly NetworkProbeService _networkProbeService = new();
    private readonly ApplicationDistributionService _distributionService;
    private bool _isInfoOpen;
    private string _infoMessage = string.Empty;
    private InfoBarSeverity _infoSeverity = InfoBarSeverity.Informational;
    private Uri? _latestReleasePage;
    private string _infoActionText = string.Empty;
    private Action? _infoAction;

    public event Action? ShowUpdatesRequested;

    public MainViewModel()
        : this(new ApplicationDistributionService())
    {
    }

    internal MainViewModel(ApplicationDistributionService distributionService)
    {
        _distributionService = distributionService;
        _storeUpdateService = new StoreUpdateService(_updateService);
        Translations = new TranslationManagementViewModel(
            () => Settings,
            Operation,
            (message, severity) => ShowInfo(message, severity));
        Translations.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TranslationManagementViewModel.DetectedSteamDirectory))
            {
                OnPropertyChanged(nameof(CurrentSteamDirectory));
            }
        };
        Translations.UpdatesDetected += (count, usedCache) => ShowInfo(
            usedCache
                ? $"根据本地目录缓存发现 {count} 个可更新译本。"
                : $"发现 {count} 个可更新译本。",
            InfoBarSeverity.Informational,
            "查看可更新",
            () =>
            {
                Translations.ShowUpdates();
                ShowUpdatesRequested?.Invoke();
            });
    }

    public ApplicationOperationState Operation { get; } = new();
    public TranslationManagementViewModel Translations { get; }
    public GuiSettings Settings { get; private set; } = new();
    public string SettingsPath => _settingsService.SettingsPath;
    public string CurrentSteamDirectory => !string.IsNullOrWhiteSpace(Settings.SteamDirectory)
        ? Settings.SteamDirectory
        : string.IsNullOrWhiteSpace(Translations.DetectedSteamDirectory)
            ? "尚未检测到 Steam 目录"
            : Translations.DetectedSteamDirectory;
    public string CurrentDataDirectory => !string.IsNullOrWhiteSpace(Settings.DataDirectory)
        ? Settings.DataDirectory
        : Path.GetDirectoryName(SettingsPath)!;
    public bool UsesStoreManagedUpdates => _distributionService.UsesStoreManagedUpdates;
    public string DistributionChannelName => _distributionService.Channel switch
    {
        ApplicationDistributionChannel.MicrosoftStore => "Microsoft Store 版",
        _ => "独立安装版",
    };
    public Uri? LatestReleasePage
    {
        get => _latestReleasePage;
        private set => SetProperty(ref _latestReleasePage, value);
    }

    public bool IsInfoOpen
    {
        get => _isInfoOpen;
        set => SetProperty(ref _isInfoOpen, value);
    }

    public string InfoMessage
    {
        get => _infoMessage;
        private set => SetProperty(ref _infoMessage, value);
    }

    public InfoBarSeverity InfoSeverity
    {
        get => _infoSeverity;
        private set => SetProperty(ref _infoSeverity, value);
    }

    public string InfoActionText
    {
        get => _infoActionText;
        private set
        {
            if (SetProperty(ref _infoActionText, value))
            {
                OnPropertyChanged(nameof(InfoActionVisibility));
            }
        }
    }

    public Visibility InfoActionVisibility => string.IsNullOrWhiteSpace(InfoActionText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public async Task InitializeAsync()
    {
        Settings = await _settingsService.LoadAsync();
        _updateService.ConfigureNetwork(Settings.Network);
        OnPropertyChanged(nameof(Settings));
        App.Logs.Configure(Settings.LoggingEnabled, Settings.LogLevel, Settings.LogRetentionDays);
        await App.Logs.WriteAsync("信息", "应用", "设置已加载，开始初始化。");
        ApplyTheme();
        await Translations.ScanAsync(refreshCatalog: true);
        if (Settings.CheckForUpdatesOnStartup)
        {
            await CheckForUpdatesCoreAsync(showCurrentResult: false);
        }
    }

    public Task<UpdateCheckResult?> CheckForUpdatesAsync()
    {
        return CheckForUpdatesCoreAsync(showCurrentResult: true);
    }

    public async Task<NetworkProbeResult> TestNetworkAsync(
        NetworkSettings settings,
        DownloadSourceSettings downloadSources)
    {
        var normalized = NetworkSettingsValidator.Normalize(settings);
        var normalizedSources = DownloadSourceCatalog.Normalize(downloadSources);
        await App.Logs.WriteAsync(
            "信息",
            "网络测试",
            $"开始测试连接。DNS={normalized.DnsMode}；代理={normalized.ProxyMode}。");
        var result = await _networkProbeService.TestAsync(normalized, normalizedSources);
        await App.Logs.WriteAsync(
            result.IsSuccess ? "信息" : "警告",
            "网络测试",
            result.Message);
        ShowInfo(
            result.Message,
            result.IsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        return result;
    }

    public async Task UpdateSettingsAsync(GuiSettings settings)
    {
        settings.Network = NetworkSettingsValidator.Normalize(settings.Network);
        settings.DownloadSources = DownloadSourceCatalog.Normalize(settings.DownloadSources);
        var previous = Settings;
        var enablingDebug = settings.LoggingEnabled
            && settings.LogLevel == "debug"
            && (!previous.LoggingEnabled || previous.LogLevel != "debug");
        await App.Logs.WriteAsync(
            "调试",
            "设置",
            $"准备保存设置。原设置={DescribeSettings(previous)}；新设置={DescribeSettings(settings)}。",
            debug: true);
        await _settingsService.SaveAsync(settings);
        Settings = settings;
        _updateService.ConfigureNetwork(settings.Network);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(CurrentSteamDirectory));
        OnPropertyChanged(nameof(CurrentDataDirectory));
        App.Logs.Configure(settings.LoggingEnabled, settings.LogLevel, settings.LogRetentionDays);
        if (enablingDebug)
        {
            await WriteDebugSessionHeaderAsync();
        }
        await App.Logs.WriteAsync(
            "调试",
            "设置",
            $"设置已应用。运行时日志级别={settings.LogLevel}；持久化日志级别=" +
            $"{(settings.LogLevel == "debug" ? "detailed（重启后自动恢复）" : settings.LogLevel)}。",
            debug: true);
        ApplyTheme();
    }

    public void ShowInfo(
        string message,
        InfoBarSeverity severity = InfoBarSeverity.Informational,
        string actionText = "",
        Action? action = null)
    {
        InfoMessage = message;
        InfoSeverity = severity;
        IsInfoOpen = true;
        InfoActionText = actionText;
        _infoAction = action;
        _ = App.Logs.WriteAsync(
            "调试",
            "界面",
            $"显示 InfoBar。严重性={severity}；消息={message}",
            debug: true);
        if (severity is InfoBarSeverity.Error or InfoBarSeverity.Warning)
        {
            _ = App.Logs.WriteAsync(
                severity == InfoBarSeverity.Error ? "错误" : "警告",
                "界面",
                message);
        }
    }

    public void InvokeInfoAction()
    {
        var action = _infoAction;
        _infoAction = null;
        InfoActionText = string.Empty;
        action?.Invoke();
    }

    private async Task<UpdateCheckResult?> CheckForUpdatesCoreAsync(bool showCurrentResult)
    {
        if (!Operation.TryBegin())
        {
            return null;
        }
        IsInfoOpen = false;
        Operation.SetStatus("正在检查软件更新…");
        var stopwatch = Stopwatch.StartNew();
        await App.Logs.WriteAsync(
            "调试",
            "更新",
            $"开始检查更新。手动显示当前结果={showCurrentResult}；当前版本={UpdateService.CurrentVersionText}。",
            debug: true);
        try
        {
            var result = UsesStoreManagedUpdates
                ? await _storeUpdateService.CheckAsync()
                : await _updateService.CheckAsync();
            LatestReleasePage = result.ReleasePage;
            await App.Logs.WriteAsync("信息", "更新", result.Message);
            await App.Logs.WriteAsync(
                "调试",
                "更新",
                $"更新检查完成。耗时={stopwatch.ElapsedMilliseconds} ms；当前={result.CurrentVersion}；" +
                $"最新={result.LatestVersion}；有更新={result.IsUpdateAvailable}；发布页={result.ReleasePage}。",
                debug: true);
            if (result.IsUpdateAvailable)
            {
                var xamlRoot = (App.Window.Content as FrameworkElement)?.XamlRoot;
                if (xamlRoot is not null)
                {
                    await UpdateDialogService.ShowAsync(xamlRoot, result, _updateService);
                }
                else
                {
                    ShowInfo(result.Message, InfoBarSeverity.Success);
                }
            }
            else if (showCurrentResult)
            {
                ShowInfo(result.Message, InfoBarSeverity.Informational);
            }
            return result;
        }
        catch (Exception exception)
        {
            var message = NetworkErrorMessage.Describe(exception, "检查软件更新");
            await App.Logs.WriteAsync("警告", "更新", message);
            await App.Logs.WriteAsync(
                "调试",
                "更新",
                $"更新检查异常。耗时={stopwatch.ElapsedMilliseconds} ms。",
                debug: true);
            await App.Logs.WriteExceptionDetailsAsync("更新", exception);
            if (showCurrentResult)
            {
                ShowInfo(message, InfoBarSeverity.Warning);
            }
            return null;
        }
        finally
        {
            Operation.Complete();
        }
    }

    private void ApplyTheme()
    {
        if (App.Window.Content is not FrameworkElement root)
        {
            return;
        }
        root.RequestedTheme = Settings.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (App.Window is MainWindow mainWindow)
        {
            var effectiveTheme = root.RequestedTheme == ElementTheme.Default
                ? root.ActualTheme
                : root.RequestedTheme;
            mainWindow.ApplyTitleBarTheme(effectiveTheme);
            App.DispatcherQueue.TryEnqueue(() => mainWindow.ApplyTitleBarTheme(root.ActualTheme));
        }
    }

    private async Task WriteDebugSessionHeaderAsync()
    {
        await App.Logs.WriteAsync(
            "调试",
            "Debug",
            $"Debug 会话已开启。会话 ID={Guid.NewGuid():N}；进程 ID={Environment.ProcessId}；" +
            $"应用版本={UpdateService.CurrentVersionText}；OS={Environment.OSVersion}；" +
            $".NET={Environment.Version}；程序目录={AppContext.BaseDirectory}；日志目录={App.Logs.DirectoryPath}；" +
            $"当前设置={DescribeSettings(Settings)}。Debug 仅本次运行有效。",
            debug: true);
    }

    private static string DescribeSettings(GuiSettings settings) =>
        $"SteamDirectory={settings.SteamDirectory}; DataDirectory={settings.DataDirectory}; Offline={settings.Offline}; " +
        $"Theme={settings.Theme}; LoggingEnabled={settings.LoggingEnabled}; LogLevel={settings.LogLevel}; " +
        $"LogRetentionDays={settings.LogRetentionDays}; LogWordWrap={settings.LogWordWrap}; " +
        $"CheckForUpdatesOnStartup={settings.CheckForUpdatesOnStartup}; " +
        $"DnsMode={settings.Network.DnsMode}; DnsServers={settings.Network.DnsServers}; " +
        $"ProxyMode={settings.Network.ProxyMode}; ProxyAddress={settings.Network.ProxyAddress}; " +
        $"ProxyUsernameConfigured={!string.IsNullOrEmpty(settings.Network.ProxyUsername)}; " +
        $"ProxyPasswordConfigured={!string.IsNullOrEmpty(settings.Network.ProxyPassword)}; " +
        $"IndexSources={DownloadSourceCatalog.EnvironmentOrder(settings.DownloadSources.IndexSourceOrder)}; " +
        $"FileSources={DownloadSourceCatalog.EnvironmentOrder(settings.DownloadSources.FileSourceOrder)}; " +
        $"SteamLibraryEnabled={settings.SteamLibrary.Enabled}; SteamIdConfigured=" +
        $"{!string.IsNullOrEmpty(settings.SteamLibrary.SteamId)}; SteamApiKeyConfigured=" +
        $"{!string.IsNullOrEmpty(settings.SteamLibrary.ApiKey)}";
}
