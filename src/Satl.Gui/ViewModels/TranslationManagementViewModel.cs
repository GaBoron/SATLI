using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.ViewModels;

public sealed class TranslationManagementViewModel : ObservableObject
{
    private readonly SatlCliService _cli = new();
    private readonly Func<GuiSettings> _settings;
    private readonly ApplicationOperationState _operation;
    private readonly Action<string, InfoBarSeverity> _showInfo;
    private readonly TranslationCliArguments _arguments;
    private string _searchText = string.Empty;
    private string _detectedSteamDirectory = string.Empty;

    public TranslationManagementViewModel(
        Func<GuiSettings> settings,
        ApplicationOperationState operation,
        Action<string, InfoBarSeverity> showInfo)
    {
        _settings = settings;
        _operation = operation;
        _showInfo = showInfo;
        _arguments = new TranslationCliArguments(settings);
    }

    public ObservableCollection<GameItem> Games { get; } = [];
    public ObservableCollection<GameItem> VisibleGames { get; } = [];
    public ObservableCollection<GameItem> ManagedGames { get; } = [];
    public string DetectedSteamDirectory
    {
        get => _detectedSteamDirectory;
        private set => SetProperty(ref _detectedSteamDirectory, value);
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

    public void ShowInfo(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) =>
        _showInfo(message, severity);

    public async Task ScanAsync(bool refreshCatalog = true)
    {
        if (!_operation.TryBegin())
        {
            return;
        }
        try
        {
            var settings = _settings();
            var refreshed = false;
            if (refreshCatalog && !settings.Offline)
            {
                refreshed = await RefreshCatalogCoreAsync();
            }
            await ScanCoreAsync(forceOffline: refreshed || settings.Offline);
            await LoadManagedCoreAsync(forceOffline: refreshed || settings.Offline);
            ShowInfo($"扫描完成，匹配到 {Games.Count} 个可用翻译。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("扫描", exception);
        }
        finally
        {
            _operation.Complete();
        }
    }

    public async Task<IReadOnlyList<ReplacementPreview>?> PreviewInstallAsync(
        IReadOnlyList<GameItem> selected)
    {
        var result = await RunPreviewAsync(
            _arguments.Install(selected, dryRun: true, yes: false, previewContent: true),
            "正在读取待安装文件内容…");
        return result is null ? null : TryParsePreviews(result, selected);
    }

    public async Task<ReplacementPreview?> PreviewCurrentAsync(GameItem game)
    {
        var result = await RunPreviewAsync(
            _arguments.SchemaInspect(game.AppId),
            "正在读取当前翻译内容…");
        if (result is null)
        {
            return null;
        }
        try
        {
            return ParseCurrentPreview(result, game);
        }
        catch (Exception exception)
        {
            ShowException("查看当前翻译", exception);
            return null;
        }
    }

    public static ReplacementPreview ParseCurrentPreview(CliRunResult result, GameItem game)
    {
        var payloads = result.Events
            .Where(item => item.Operation == "schema-inspect" && item.Event == "item-succeeded")
            .Select(item => item.Payload)
            .ToList();
        if (payloads.Count != 1)
        {
            throw new InvalidDataException(
                $"当前翻译预览数量无效：期望 1 个，收到 {payloads.Count} 个。拒绝继续。"
            );
        }
        var preview = ReplacementPreview.FromPayload(payloads[0], game.GameName);
        if (!preview.AppId.Equals(game.AppId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("当前翻译预览的 App ID 与所选游戏不一致。拒绝继续。");
        }
        return preview with
        {
            GameName = game.GameName,
            VariantId = string.IsNullOrWhiteSpace(game.InstalledVariantId)
                ? "当前文件"
                : game.InstalledVariantId,
        };
    }

    public async Task InstallAsync(IReadOnlyList<GameItem> selected)
    {
        if (!_operation.TryBegin())
        {
            return;
        }
        try
        {
            var result = await RunCliAsync(
                _arguments.Install(selected, dryRun: false, yes: true, previewContent: false),
                "正在安装翻译…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await ReloadAfterMutationAsync();
            ShowInfo("所选翻译已安装。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("安装", exception);
        }
        finally
        {
            _operation.Complete();
        }
    }

    public async Task<IReadOnlyList<ReplacementPreview>?> PreviewRestoreAsync(
        IReadOnlyList<GameItem> selected,
        bool force)
    {
        var result = await RunPreviewAsync(
            _arguments.Restore(selected, dryRun: true, yes: false, force, previewContent: true),
            "正在读取待恢复文件内容…");
        return result is null ? null : TryParsePreviews(result, selected);
    }

    public async Task RestoreAsync(IReadOnlyList<GameItem> selected, bool force)
    {
        if (!_operation.TryBegin())
        {
            return;
        }
        try
        {
            var result = await RunCliAsync(
                _arguments.Restore(selected, dryRun: false, yes: true, force, previewContent: false),
                force ? "正在强制恢复并归档当前文件…" : "正在恢复安装前文件…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await ReloadAfterMutationAsync();
            ShowInfo(force ? "已归档当前文件并完成恢复。" : "已恢复安装前文件。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("恢复", exception);
        }
        finally
        {
            _operation.Complete();
        }
    }

    public async Task RefreshCacheAsync()
    {
        if (!_operation.TryBegin())
        {
            return;
        }
        try
        {
            var result = await RunCliAsync(_arguments.CacheRefresh(), "正在刷新翻译目录…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await ReloadAfterMutationAsync();
            ShowInfo("翻译目录缓存已刷新。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("刷新缓存", exception);
        }
        finally
        {
            _operation.Complete();
        }
    }

    public async Task ExportPetitionAsync(string appId, string outputPath)
    {
        if (!_operation.TryBegin())
        {
            return;
        }
        try
        {
            var result = await RunCliAsync(
                _arguments.PetitionExport(appId, outputPath),
                $"正在导出 App ID {appId} 的翻译请愿文件…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            ShowInfo($"翻译请愿 ZIP 已导出：{outputPath}", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("导出请愿文件", exception);
        }
        finally
        {
            _operation.Complete();
        }
    }

    private async Task<CliRunResult?> RunPreviewAsync(IReadOnlyList<string> arguments, string status)
    {
        if (!_operation.TryBegin())
        {
            return null;
        }
        try
        {
            var result = await RunCliAsync(arguments, status);
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return null;
            }
            return result;
        }
        catch (Exception exception)
        {
            ShowException("预览", exception);
            return null;
        }
        finally
        {
            _operation.Complete();
        }
    }

    private IReadOnlyList<ReplacementPreview>? TryParsePreviews(
        CliRunResult result,
        IReadOnlyList<GameItem> selected)
    {
        try
        {
            var selectedById = selected.ToDictionary(item => item.AppId);
            var previews = result.Events
                .Where(item => item.Event == "item-preview")
                .Select(item =>
                {
                    var appId = item.Payload.TryGetProperty("app_id", out var value)
                        ? value.GetString() ?? string.Empty
                        : string.Empty;
                    var fallbackName = selectedById.TryGetValue(appId, out var game)
                        ? game.GameName
                        : appId;
                    return ReplacementPreview.FromPayload(item.Payload, fallbackName);
                })
                .ToList();
            if (previews.Count != selected.Count)
            {
                throw new InvalidDataException(
                    $"替换预览数量不完整：请求 {selected.Count} 个，收到 {previews.Count} 个。拒绝继续。"
                );
            }
            return previews;
        }
        catch (Exception exception)
        {
            ShowException("替换预览", exception);
            return null;
        }
    }

    private async Task<bool> RefreshCatalogCoreAsync()
    {
        var result = await RunCliAsync(_arguments.CacheRefresh(), "正在刷新云端翻译索引…");
        if (result.IsSuccess)
        {
            return true;
        }
        ShowInfo(
            "云端索引刷新失败，将尝试使用已验证的本地缓存。" + Environment.NewLine + ResultError(result),
            InfoBarSeverity.Warning);
        return false;
    }

    private async Task ScanCoreAsync(bool forceOffline = false)
    {
        var arguments = _arguments.Scan(forceOffline, out var steamLibraryWarning);
        if (steamLibraryWarning is not null)
        {
            ShowInfo(steamLibraryWarning, InfoBarSeverity.Warning);
        }
        var result = await RunCliAsync(arguments, "正在扫描本地 Steam 数据…");
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(ResultError(result));
        }

        var plan = result.Events.FirstOrDefault(item => item.Event == "plan");
        if (plan is not null
            && plan.Payload.TryGetProperty("steam_dir", out var steamDirectory)
            && steamDirectory.ValueKind == JsonValueKind.String)
        {
            DetectedSteamDirectory = steamDirectory.GetString() ?? string.Empty;
        }

        Games.Clear();
        foreach (var satlEvent in result.Events.Where(item => item.Event == "item-succeeded"))
        {
            Games.Add(GameItem.FromPayload(satlEvent.Payload));
        }
        ApplyFilter();
    }

    private async Task LoadManagedCoreAsync(bool forceOffline = false)
    {
        var result = await RunCliAsync(
            _arguments.Status(forceOffline),
            "正在读取安装状态…");
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(ResultError(result));
        }

        ManagedGames.Clear();
        foreach (var satlEvent in result.Events.Where(item => item.Event == "item-succeeded"))
        {
            var managed = GameItem.FromPayload(satlEvent.Payload);
            ManagedGames.Add(managed);
            var scanned = Games.FirstOrDefault(item => item.AppId == managed.AppId);
            if (scanned is not null)
            {
                scanned.InstalledState = managed.InstalledState;
                scanned.InstalledVariantId = managed.InstalledVariantId;
                scanned.InstalledSource = managed.InstalledSource;
            }
        }
    }

    private async Task ReloadAfterMutationAsync()
    {
        await ScanCoreAsync(forceOffline: true);
        await LoadManagedCoreAsync(forceOffline: true);
    }

    private async Task<CliRunResult> RunCliAsync(IReadOnlyList<string> arguments, string status)
    {
        _operation.SetStatus(status);
        var operation = arguments.FirstOrDefault() ?? "unknown";
        var tracksGameLoading = operation == "scan";
        if (tracksGameLoading)
        {
            _operation.GameLoading.Start(status);
        }
        var stopwatch = Stopwatch.StartNew();
        await App.Logs.WriteAsync("信息", operation, $"开始：{status}");
        await App.Logs.WriteAsync(
            "调试", operation,
            $"GUI 已提交 CLI 操作。状态文本={status}；参数数量={arguments.Count}。",
            debug: true);
        var diagnosticWrites = new List<Task>();
        Action<string>? diagnostic = App.Logs.IsDebugEnabled
            ? message => diagnosticWrites.Add(App.Logs.WriteAsync("调试", operation, message, debug: true))
            : null;
        CliRunResult result;
        try
        {
            var settings = _settings();
            result = await _cli.RunAsync(arguments, satlEvent =>
            {
                _ = App.Logs.WriteAsync("详细", satlEvent.Operation, DescribeEvent(satlEvent), detailed: true);
                if (tracksGameLoading)
                {
                    void UpdateProgress()
                    {
                        _operation.GameLoading.Handle(satlEvent);
                        if (_operation.GameLoading.IsActive)
                        {
                            _operation.SetStatus(_operation.GameLoading.Text);
                        }
                    }
                    if (App.DispatcherQueue.HasThreadAccess)
                    {
                        UpdateProgress();
                    }
                    else
                    {
                        App.DispatcherQueue.TryEnqueue(UpdateProgress);
                    }
                }
                if (satlEvent.Event == "item-started"
                    && satlEvent.Payload.TryGetProperty("app_id", out var appId))
                {
                    App.DispatcherQueue.TryEnqueue(
                        () => _operation.SetStatus($"正在处理 App ID {appId.GetString()}…"));
                }
                else if (satlEvent.Event == "warning"
                    && satlEvent.Payload.TryGetProperty("message", out var warning))
                {
                    App.DispatcherQueue.TryEnqueue(
                        () => ShowInfo(warning.GetString() ?? "正在使用本地缓存。"));
                }
            }, diagnostic, settings.Network, settings.SteamLibrary);
        }
        catch (Exception exception)
        {
            if (tracksGameLoading)
            {
                _operation.GameLoading.Fail("游戏加载失败");
            }
            await Task.WhenAll(diagnosticWrites);
            await App.Logs.WriteAsync(
                "调试", operation,
                $"CLI 调用抛出异常。耗时={stopwatch.ElapsedMilliseconds} ms。{exception}",
                debug: true);
            throw;
        }
        await Task.WhenAll(diagnosticWrites);
        if (tracksGameLoading)
        {
            _operation.GameLoading.Finish(result.IsSuccess ? "游戏加载完成" : "游戏加载失败");
        }
        await App.Logs.WriteAsync(
            result.IsSuccess ? "信息" : "错误",
            operation,
            $"完成：退出码 {result.ExitCode}，事件 {result.Events.Count} 个。" +
            (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty : $" 标准错误：{result.StandardError}"));
        await App.Logs.WriteAsync(
            "调试", operation,
            $"CLI 操作返回 GUI。成功={result.IsSuccess}；耗时={stopwatch.ElapsedMilliseconds} ms；" +
            $"错误消息={result.ErrorMessage}。",
            debug: true);
        return result;
    }

    private void ApplyFilter()
    {
        VisibleGames.Clear();
        var query = SearchText.Trim();
        foreach (var game in Games.Where(game =>
                     string.IsNullOrWhiteSpace(query)
                     || game.GameName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || game.AppId.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            VisibleGames.Add(game);
        }
    }

    private static string DescribeEvent(SatlEvent satlEvent)
    {
        var appId = satlEvent.Payload.TryGetProperty("app_id", out var appIdValue)
            ? $"，App ID {appIdValue.GetString()}"
            : string.Empty;
        var variant = satlEvent.Payload.TryGetProperty("variant_id", out var variantValue)
            ? $"，版本 {variantValue.GetString()}"
            : string.Empty;
        var message = satlEvent.Payload.TryGetProperty("message", out var messageValue)
            ? $"：{messageValue.GetString()}"
            : string.Empty;
        return $"事件 {satlEvent.Event}{appId}{variant}{message}";
    }

    private void ShowException(string operation, Exception exception)
    {
        _ = App.Logs.WriteAsync("调试", operation, exception.ToString(), debug: true);
        var message = NetworkErrorMessage.IsNetworkError(exception)
            ? NetworkErrorMessage.Describe(exception, operation)
            : exception.Message;
        ShowInfo(message, InfoBarSeverity.Error);
    }

    private void ShowResultError(CliRunResult result) =>
        ShowInfo(ResultError(result), InfoBarSeverity.Error);

    private static string ResultError(CliRunResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? $"SATL 操作失败，退出码 {result.ExitCode}。"
            : result.ErrorMessage;
}
