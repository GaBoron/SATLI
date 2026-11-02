using Microsoft.UI.Xaml.Controls;
using Satli.Core.SatliLite;
using Satli.Core.TranslationProjects;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.ViewModels;

public sealed partial class TranslationManagementViewModel
{
    private string _liteLibraryMessage = "";
    private bool _readingLiteLibrary;
    private long _liteLibraryTime;
    public string LiteLibraryMessage { get => _liteLibraryMessage; private set => SetProperty(ref _liteLibraryMessage, value); }

    public async Task RefreshLiteLibraryAsync(bool quiet = false)
    {
        if (_readingLiteLibrary) return;
        _readingLiteLibrary = true;
        try
        {
            if (!quiet) await App.Logs.WriteAsync("信息", "SATLI-lite", "开始读取公开已安装译文清单。");
            await LoadLiteLibraryCoreAsync();
        }
        catch (Exception exception)
        {
            LiteLibraryMessage = "读取 lite 清单失败，请检查 Steam 路径和插件状态后刷新。";
            if (!quiet) await App.Logs.WriteAsync("警告", "SATLI-lite", $"公开清单读取失败；异常类型={exception.GetType().Name}。");
        }
        finally { _readingLiteLibrary = false; }
    }

    private async Task LoadLiteLibraryCoreAsync()
    {
        var client = LiteWorkflowService.Client(_settings());
        var snapshot = await Task.Run(() => new LiteLibraryReader(client.SteamDirectory).ReadAsync());
        if (snapshot is null)
        {
            LiteLibraryMessage = "当前插件尚未提供公开清单。更新 lite 并重启 Steam 后，可读取已安装译文并快捷编辑。";
            return;
        }
        var ids = snapshot.Apps.Select(app => app["app_id"]!.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (var game in ManagedGames.Where(game => !ids.Contains(game.AppId)))
        {
            game.LiteInstalled = false;
            game.LiteReadable = false;
        }
        foreach (var app in snapshot.Apps)
        {
            var id = app["app_id"]!.ToString();
            var game = ManagedGames.FirstOrDefault(item => item.AppId == id)
                ?? Games.FirstOrDefault(item => item.AppId == id)
                ?? new GameItem { AppId = id, GameName = app["name"]?.ToString() ?? $"Steam 游戏 {id}" };
            game.LiteInstalled = true;
            game.LiteKnown = true;
            game.LiteEnabled = app["effective_enabled"]!.GetValue<bool>();
            game.LiteVariantId = app["variant_id"]!.ToString();
            game.LiteLanguage = app["language"]!.ToString();
            game.LiteReadable = app["translation_file"] is not null;
            game.LiteStateText = app["local_edits"]?.GetValue<bool>() == true ? "含本地修改" : "社区译本";
            if (!ManagedGames.Any(item => item.AppId == id)) ManagedGames.Add(game);
        }
        foreach (var game in ManagedGames.Where(game => game.LitePending).ToArray())
        {
            var result = await client.ResumeAsync(game.AppId, TimeSpan.Zero);
            if (result is not null) ObserveLite(game, result);
        }
        LiteLibraryMessage = $"已读取 {snapshot.Apps.Count} 款游戏 · 快照更新于 {DateTimeOffset.FromUnixTimeSeconds(snapshot.GeneratedAt).ToLocalTime():g}";
        if (_liteLibraryTime != snapshot.GeneratedAt)
        {
            _liteLibraryTime = snapshot.GeneratedAt;
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"公开清单已更新；游戏={snapshot.Apps.Count}。");
        }
    }

    public async Task<TranslationProject?> OpenLiteEditorAsync(GameItem game)
    {
        try
        {
            var client = LiteWorkflowService.Client(_settings());
            var translation = await Task.Run(() => new LiteLibraryReader(client.SteamDirectory).ReadTranslationAsync(game.AppId));
            var project = await Task.Run(() => TranslationProject.Create(translation, game.GameName,
                game.LiteLanguage, "SATLI-lite 公开译文快照"));
            var store = TranslationProjectService.Store();
            var saved = await store.LoadAllAsync();
            project = saved.Projects.FirstOrDefault(item => item.AppId == project.AppId
                && item.VariantId == project.VariantId && item.TargetLanguage == project.TargetLanguage
                && item.SourceFingerprint == project.SourceFingerprint && item.Source == project.Source) ?? project;
            await store.SaveAsync(project);
            await App.Logs.WriteAsync("信息", "SATLI-lite", $"已打开译文编辑。App ID={game.AppId}。");
            return project;
        }
        catch (Exception exception)
        {
            ShowInfo(exception.Message, InfoBarSeverity.Warning);
            await App.Logs.WriteAsync("警告", "SATLI-lite", $"译文读取失败；异常类型={exception.GetType().Name}。");
            return null;
        }
    }
}
