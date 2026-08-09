using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.ViewModels;

public sealed partial class TranslationManagementViewModel
{
    public async Task ScanAsync(bool refreshCatalog = true)
    {
        if (!_operation.TryBegin())
        {
            return;
        }
        BeginLoading();
        try
        {
            var settings = _settings();
            var refreshed = false;
            if (refreshCatalog && !settings.Offline)
            {
                refreshed = await RefreshCatalogCoreAsync();
            }
            await ScanCoreAsync(useCatalogCache: refreshed);
            await LoadManagedCoreAsync(forceOffline: refreshed || settings.Offline);
            if (refreshCatalog && UpdateAvailableCount > 0)
            {
                UpdatesDetected?.Invoke(UpdateAvailableCount, !refreshed);
            }
            else if (UpdateAvailableCount == 0 || !refreshCatalog)
            {
                ShowInfo($"扫描完成，匹配到 {Games.Count} 个可用翻译。", InfoBarSeverity.Success);
            }
        }
        catch (Exception exception)
        {
            ShowException("扫描", exception);
        }
        finally
        {
            CompleteLoading();
            _operation.Complete();
        }
    }

    public async Task<IReadOnlyList<ReplacementPreview>?> PreviewInstallAsync(
        IReadOnlyList<GameItem> selected) =>
        await PreviewCatalogEntriesAsync(selected, "正在读取待安装文件内容…");

    public async Task<ReplacementPreview?> PreviewCatalogAsync(GameItem game)
    {
        var previews = await PreviewCatalogEntriesAsync([game], "正在读取云端成就…");
        return previews?.SingleOrDefault();
    }

    private async Task<IReadOnlyList<ReplacementPreview>?> PreviewCatalogEntriesAsync(
        IReadOnlyList<GameItem> selected,
        string status)
    {
        var result = await RunPreviewAsync(
            _arguments.Install(selected, dryRun: true, yes: false, previewContent: true),
            status);
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
            return TranslationPreviewParser.ParseCurrent(result, game);
        }
        catch (Exception exception)
        {
            ShowException("查看当前翻译", exception);
            return null;
        }
    }

    public static ReplacementPreview ParseCurrentPreview(CliRunResult result, GameItem game) =>
        TranslationPreviewParser.ParseCurrent(result, game);

    public async Task InstallAsync(IReadOnlyList<GameItem> selected)
    {
        if (!_operation.TryBegin())
        {
            return;
        }
        try
        {
            using var monitoringSuppression = BeginSchemaMonitoringSuppression(
                selected.Select(item => item.AppId));
            var result = await RunCliAsync(
                _arguments.Install(selected, dryRun: false, yes: true, previewContent: false),
                "正在安装翻译…");
            var summary = InstallOperationSummary.TryCreate(result);
            if (summary is not null)
            {
                if (summary.HasSucceededItems)
                {
                    await ReloadAfterMutationAsync();
                }
                ShowInfo(
                    summary.Message,
                    summary.Failed == 0
                        ? InfoBarSeverity.Success
                        : summary.HasSucceededItems
                            ? InfoBarSeverity.Warning
                            : InfoBarSeverity.Error);
                return;
            }
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
            using var monitoringSuppression = BeginSchemaMonitoringSuppression(
                selected.Select(item => item.AppId));
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

    public async Task<bool> ExportPetitionAsync(string appId, string outputPath)
    {
        if (!_operation.TryBegin())
        {
            return false;
        }
        try
        {
            var result = await RunCliAsync(
                _arguments.PetitionExport(appId, outputPath),
                $"正在导出 App ID {appId} 的翻译请愿文件…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return false;
            }
            ShowInfo($"翻译请愿 ZIP 已导出：{outputPath}", InfoBarSeverity.Success);
            return true;
        }
        catch (Exception exception)
        {
            ShowException("导出请愿文件", exception);
            return false;
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
            return TranslationPreviewParser.ParseBatch(result, selected);
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

    private async Task ScanCoreAsync(bool useCatalogCache = false)
    {
        var arguments = _arguments.Scan(useCatalogCache, out var steamLibraryWarning);
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
        foreach (var satliEvent in result.Events.Where(item => item.Event == "item-succeeded"))
        {
            Games.Add(GameItem.FromPayload(satliEvent.Payload));
        }
        ApplyFilter();
    }

    private async Task LoadManagedCoreAsync(bool forceOffline = false)
    {
        var result = await RunCliAsync(_arguments.Status(forceOffline), "正在读取安装状态…");
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(ResultError(result));
        }

        ManagedGames.Clear();
        foreach (var satliEvent in result.Events.Where(item => item.Event == "item-succeeded"))
        {
            var managed = GameItem.FromPayload(satliEvent.Payload);
            ManagedGames.Add(managed);
            var scanned = Games.FirstOrDefault(item => item.AppId == managed.AppId);
            if (scanned is not null)
            {
                scanned.InstalledState = managed.InstalledState;
                scanned.InstalledVariantId = managed.InstalledVariantId;
                scanned.InstalledSource = managed.InstalledSource;
                scanned.InstalledAt = managed.InstalledAt;
                scanned.InstalledSha256 = managed.InstalledSha256;
                scanned.FileReadOnly = managed.FileReadOnly;
            }
        }
        UpdateAvailableCount = GameInstallFiltering.CountUpdates(Games);
        ApplyFilter();
    }

    private async Task ReloadAfterMutationAsync()
    {
        await ScanCoreAsync(useCatalogCache: true);
        await LoadManagedCoreAsync(forceOffline: true);
    }
}
