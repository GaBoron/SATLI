using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.ViewModels;

public sealed class GameInventoryViewModel : ObservableObject
{
    private readonly GameInventoryScope _scope;
    private readonly IGameInventoryLoader _loader;
    private readonly GameInventoryPreloader _preloader;
    private string _searchText = string.Empty;
    private string _statusMessage = "准备就绪";
    private bool _isBusy;
    private bool _isLoading = true;
    private bool _initialized;
    private GameInstallFilterOption _selectedFilterOption;

    public GameInventoryViewModel(GameInventoryScope scope)
        : this(scope, new GameInventoryLoader(), App.InventoryPreloader)
    {
    }

    internal GameInventoryViewModel(
        GameInventoryScope scope,
        IGameInventoryLoader loader,
        GameInventoryPreloader preloader)
    {
        _scope = scope;
        _loader = loader;
        _preloader = preloader;
        _selectedFilterOption = GameInstallFiltering.OptionsFor(scope)[0];
    }

    public ObservableCollection<GameItem> Games { get; } = [];
    public ObservableCollection<GameItem> VisibleGames { get; } = [];
    public GameLoadingProgress Loading { get; } = new();
    public IReadOnlyList<GameInstallFilterOption> FilterOptions =>
        GameInstallFiltering.OptionsFor(_scope);

    public GameInstallFilterOption SelectedFilterOption
    {
        get => _selectedFilterOption;
        set
        {
            if (value is not null && SetProperty(ref _selectedFilterOption, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(GameListVisibility));
                OnPropertyChanged(nameof(EmptyStateVisibility));
            }
        }
    }

    public Visibility GameListVisibility => IsLoading ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EmptyStateVisibility => !IsLoading && VisibleGames.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await LoadAsync(_preloader.Take(_scope));
    }

    public Task RefreshAsync() => LoadAsync(preload: null);

    private async Task LoadAsync(Task<GameInventoryPreloadAttempt>? preload)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsLoading = true;
        StatusMessage = preload is null
            ? _scope == GameInventoryScope.Local
                ? "正在扫描本地 Steam 游戏…"
                : "正在读取云端翻译索引…"
            : $"正在等待{ScopeLabel()}游戏清单后台预加载…";
        Loading.Start(StatusMessage);
        var stopwatch = Stopwatch.StartNew();
        var usedPreload = false;
        try
        {
            await App.Logs.WriteAsync("信息", "游戏清单", StatusMessage);
            GameInventorySnapshot snapshot;
            if (preload is not null)
            {
                var attempt = await preload;
                if (attempt.Snapshot is not null)
                {
                    snapshot = attempt.Snapshot;
                    usedPreload = true;
                }
                else
                {
                    await App.Logs.WriteAsync(
                        "详细",
                        "游戏清单",
                        $"{ScopeLabel()}后台预加载不可用，开始前台重试。",
                        detailed: true);
                    snapshot = await _loader.LoadAsync(
                        _scope,
                        App.ViewModel.Settings,
                        HandleEvent);
                }
            }
            else
            {
                snapshot = await _loader.LoadAsync(
                    _scope,
                    App.ViewModel.Settings,
                    HandleEvent);
            }

            Games.Clear();
            foreach (var game in snapshot.Games)
            {
                Games.Add(game);
            }
            if (!string.IsNullOrWhiteSpace(snapshot.ConfigurationWarning))
            {
                App.ViewModel.ShowInfo(snapshot.ConfigurationWarning, InfoBarSeverity.Warning);
            }
            if (usedPreload)
            {
                foreach (var satliEvent in snapshot.Events.Where(
                             item => item.Event is "warning" or "plan"))
                {
                    HandleMetadataEvent(satliEvent);
                }
            }
            ApplyFilter();
            StatusMessage = $"共 {Games.Count} 个{(_scope == GameInventoryScope.Local ? "本地游戏" : "云端条目")}";
            Loading.Finish(StatusMessage);
            await App.Logs.WriteAsync("信息", "游戏清单", StatusMessage);
            await App.Logs.WriteAsync(
                "调试",
                "游戏清单",
                $"清单加载完成。范围={_scope}；数量={Games.Count}；前台耗时={stopwatch.ElapsedMilliseconds} ms；" +
                $"数据加载耗时={snapshot.ElapsedMilliseconds} ms；使用后台预加载={usedPreload}。",
                debug: true);
        }
        catch (Exception exception)
        {
            StatusMessage = "加载失败";
            Loading.Fail(StatusMessage);
            App.ViewModel.ShowInfo(exception.Message, InfoBarSeverity.Error);
            await App.Logs.WriteAsync(
                "调试",
                "游戏清单",
                $"清单加载失败。范围={_scope}；耗时={stopwatch.ElapsedMilliseconds} ms。",
                debug: true);
            await App.Logs.WriteExceptionDetailsAsync("游戏清单", exception);
        }
        finally
        {
            IsLoading = false;
            IsBusy = false;
        }
    }

    private void HandleEvent(SatliEvent satliEvent)
    {
        void UpdateUi()
        {
            Loading.Handle(satliEvent);
            if (Loading.IsActive)
            {
                StatusMessage = Loading.Text;
            }
            HandleMetadataEvent(satliEvent);
        }

        if (App.DispatcherQueue.HasThreadAccess)
        {
            UpdateUi();
        }
        else
        {
            App.DispatcherQueue.TryEnqueue(UpdateUi);
        }
    }

    private static void HandleMetadataEvent(SatliEvent satliEvent)
    {
        if (satliEvent.Event == "warning"
            && satliEvent.Payload.TryGetProperty("message", out var warning))
        {
            var message = warning.GetString() ?? "正在使用本地缓存。";
            App.ViewModel.ShowInfo(message, InfoBarSeverity.Warning);
            _ = App.Logs.WriteAsync("警告", "游戏清单", message);
            return;
        }
        if (satliEvent.Event != "plan"
            || !satliEvent.Payload.TryGetProperty("catalog_version", out var version))
        {
            return;
        }

        var catalogVersion = version.TryGetInt32(out var parsed) ? parsed : 0;
        var fromCache = satliEvent.Payload.TryGetProperty("catalog_from_cache", out var cached)
            && cached.ValueKind is System.Text.Json.JsonValueKind.True;
        _ = App.Logs.WriteAsync(
            "详细",
            "游戏清单",
            $"翻译目录状态：可用={catalogVersion > 0}；版本={catalogVersion}；缓存={fromCache}。",
            detailed: true);
        if (satliEvent.Payload.TryGetProperty("parallel_preparation", out var parallel)
            && satliEvent.Payload.TryGetProperty("catalog_elapsed_ms", out var catalogElapsed)
            && satliEvent.Payload.TryGetProperty("steam_discovery_elapsed_ms", out var steamElapsed))
        {
            _ = App.Logs.WriteAsync(
                "详细",
                "游戏清单",
                $"扫描准备：并行={parallel.ValueKind is System.Text.Json.JsonValueKind.True}；" +
                $"目录={catalogElapsed.GetInt64()} ms；Steam={steamElapsed.GetInt64()} ms。",
                detailed: true);
        }
        if (satliEvent.Payload.TryGetProperty("catalog_source", out var source))
        {
            _ = App.Logs.WriteAsync(
                "调试",
                "游戏清单",
                $"翻译目录来源={source.GetString() ?? string.Empty}。",
                debug: true);
        }
    }

    private void ApplyFilter()
    {
        VisibleGames.Clear();
        var query = SearchText.Trim();
        foreach (var game in Games.Where(game =>
                     GameInstallFiltering.Matches(game, SelectedFilterOption.Value, _scope)
                     && (string.IsNullOrWhiteSpace(query)
                         || game.GameName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                         || game.AppId.Contains(query, StringComparison.OrdinalIgnoreCase))))
        {
            VisibleGames.Add(game);
        }
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private string ScopeLabel() =>
        _scope == GameInventoryScope.Local ? "本地" : "云端";
}
