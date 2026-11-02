using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.Pages;

public sealed partial class AchievementEditorPage
{
    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (_inspection is null || _isBusy) return;
        await RunBusyAsync(async () =>
        {
            SchemaEditResult result;
            try
            {
                var destination = await TranslationJsonExportService.PickAsync(XamlRoot,
                    _inspection.AppId, _inspection.VariantId);
                if (destination is null) return;
                result = _project is null
                    ? await _editor.ExportAsync(_inspection, _targetLanguage,
                        _inspection.Rows, true, "json", destination.Output, destination.VariantId)
                    : await TranslationProjectService.ExportAsync(_inspection, _targetLanguage,
                        _inspection.Rows, destination.Output, destination.VariantId);
                await TranslationJsonExportService.LogCompletedAsync(_inspection.AppId, destination);
                App.ViewModel.ShowInfo($"已导出翻译 JSON：{result.Output}", InfoBarSeverity.Success);
            }
            catch (Exception exception)
            {
                await TranslationJsonExportService.ReportFailureAsync(exception);
                return;
            }
            try
            {
                await OfferContributionAsync(result);
            }
            catch (Exception exception)
            {
                App.ViewModel.ShowInfo($"JSON 已导出，但投稿准备未完成：{exception.Message}", InfoBarSeverity.Warning);
                await App.Logs.WriteAsync("警告", "翻译投稿", "JSON 导出成功，但投稿准备或打开表单失败。");
                await App.Logs.WriteAsync("详细", "翻译投稿", $"异常类型={exception.GetType().Name}。", detailed: true);
            }
        });
    }

    private async void SendLite_Click(object sender, RoutedEventArgs e)
    {
        if (_inspection is null || _game is null || !await ConfirmIncompleteAsync("发送到 SATLI-lite")) return;
        if (_project is not null)
        {
            if (!await SaveDraftCheckpointAsync(force: true)) return;
            try { await Task.Run(() => _project.Build(forPublication: true)); }
            catch (Exception exception)
            {
                App.ViewModel.ShowInfo($"译文校验未通过：{exception.Message}", InfoBarSeverity.Error);
                return;
            }
        }
        var rows = _inspection.Rows.Select(row => new Satli.Core.Formats.AchievementPreviewRow(row.Index, row.ApiName,
            new Dictionary<string, Satli.Core.Formats.AchievementTranslation>(StringComparer.Ordinal)
            {
                [_targetLanguage] = new(row.TargetName, row.TargetDescription),
            })).ToArray();
        await RunBusyAsync(() => App.ViewModel.Translations.SendEditedLiteAsync(XamlRoot, _game, _targetLanguage, rows,
            _project?.VariantId ?? "local-edit"));
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveChangesAsync();
    }

    private async Task<bool> SaveChangesAsync()
    {
        if (_project is not null) return await SaveDraftCheckpointAsync(force: true);
        if (!await ConfirmIncompleteAsync("保存到本机"))
        {
            return false;
        }
        if (!await ConfirmAsync(
                "写回本地成就文件",
                "应用会在写回前创建可恢复备份。",
                "确认写回"))
        {
            return false;
        }
        var saved = false;
        await _steamMutations.ExecuteAsync(XamlRoot, async () =>
        {
            await RunBusyAsync(async () =>
            {
                using var monitoringSuppression = App.ViewModel.Translations
                    .BeginSchemaMonitoringSuppression([_inspection!.AppId]);
                var result = await _editor.ApplyAsync(
                    _inspection!, _targetLanguage, _inspection!.Rows, allowIncomplete: true);
                await App.Logs.WriteAsync(
                    "信息",
                    "成就编辑",
                    $"本地编辑已写回；修改 {result.ChangedFields} 个字段。" +
                    (string.IsNullOrWhiteSpace(result.RevisionWarning) ? "" : $" {result.RevisionWarning}"));
                await App.Logs.WriteAsync(
                    "详细",
                    "成就编辑",
                    $"写回完成。App ID={_inspection.AppId}；目标语言={_targetLanguage}；" +
                    $"备份文件={Path.GetFileName(result.Backup)}。",
                    detailed: true);
                App.ViewModel.ShowInfo(
                    string.IsNullOrWhiteSpace(result.RevisionWarning)
                        ? $"已保存本地编辑并记录 Git 修订：修改 {result.ChangedFields} 个字段；备份位于 {result.Backup}"
                        : $"已保存本地编辑：修改 {result.ChangedFields} 个字段；{result.RevisionWarning}",
                    string.IsNullOrWhiteSpace(result.RevisionWarning)
                        ? InfoBarSeverity.Success
                        : InfoBarSeverity.Warning);
                try
                {
                    _drafts.Delete(_inspection, _targetLanguage);
                }
                catch (Exception exception)
                {
                    await App.Logs.WriteAsync("警告", "成就草稿", $"本机写回成功，但删除草稿失败：{exception.Message}");
                    await App.Logs.WriteExceptionDetailsAsync("成就草稿", exception);
                }
                _draftNeedsRevision = false;
                await LoadCoreAsync();
                saved = true;
            });
            return saved;
        });
        return saved;
    }

    private async void ExportBin_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("bin");

    private async void ExportZip_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("zip");

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (_game is not null)
        {
            Frame.Navigate(typeof(RevisionHistoryPage), _game);
        }
    }

    private async Task ExportAsync(string format)
    {
        if (_project is not null) return;
        if (!await ConfirmIncompleteAsync(format == "bin" ? "导出 BIN" : "导出投稿 ZIP"))
        {
            return;
        }
        var output = await PickDestinationAsync(format);
        if (output is null)
        {
            return;
        }
        await RunBusyAsync(async () =>
        {
            var result = await _editor.ExportAsync(
                _inspection!, _targetLanguage, _inspection!.Rows, true, format, output);
            await App.Logs.WriteAsync(
                "信息",
                "成就导出",
                $"已导出 {format.ToUpperInvariant()} 文件。" +
                (string.IsNullOrWhiteSpace(result.RevisionWarning) ? "" : $" {result.RevisionWarning}"));
            await App.Logs.WriteAsync(
                "详细",
                "成就导出",
                $"导出完成。App ID={_inspection.AppId}；目标语言={_targetLanguage}；" +
                $"文件={Path.GetFileName(result.Output)}。",
                detailed: true);
            App.ViewModel.ShowInfo(
                string.IsNullOrWhiteSpace(result.RevisionWarning)
                    ? $"已导出并记录 Git 修订：{result.Output}"
                    : $"已导出：{result.Output}；{result.RevisionWarning}",
                string.IsNullOrWhiteSpace(result.RevisionWarning)
                    ? InfoBarSeverity.Success
                    : InfoBarSeverity.Warning);
            if (format == "zip" && string.IsNullOrWhiteSpace(result.RevisionWarning))
            {
                await OfferContributionAsync(result);
            }
        });
    }

    private async Task OfferContributionAsync(SchemaEditResult result)
    {
        var catalogGame = App.ViewModel.Translations.Games.FirstOrDefault(game => game.AppId == _game!.AppId) ?? _game!;
        var draft = await _contributions.PrepareAsync(catalogGame, result, _project is null ? null :
            $"来源：{_project.Source}\n获取时间：{_project.RetrievedAt:O}\n请人工审校译文；公开列表可能不含隐藏或未公开成就。");
        var attachmentType = Path.GetExtension(draft.AttachmentPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? "JSON" : "ZIP";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "投稿到社区",
            Content = $"{attachmentType} 已完成结构、语言字段和完整性校验。\n\n"
                + $"类型：{(draft.IsUpdate ? "更新已有译本" : "新译本投稿")}\n"
                + $"检测到的语言：{draft.Languages}\n"
                + $"摘要：{draft.Summary}\n\n"
                + $"打开表单后，请把资源管理器中选中的 {attachmentType} 拖入附件区域，并由你确认提交。",
            PrimaryButtonText = "打开投稿表单",
            CloseButtonText = "稍后投稿",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _contributions.OpenAsync(draft);
            await App.Logs.WriteAsync("信息", "翻译投稿", "已打开预填的 GitHub 投稿表单。");
            await App.Logs.WriteAsync(
                "详细",
                "翻译投稿",
                $"投稿已准备。App ID={_game!.AppId}；游戏={_game.GameName}；" +
                $"类型={(draft.IsUpdate ? "更新" : "新投稿")}；语言={draft.Languages}；" +
                $"文件={Path.GetFileName(draft.AttachmentPath)}。",
                detailed: true);
        }
        else
        {
            await App.Logs.WriteAsync(
                "详细",
                "翻译投稿",
                $"用户稍后投稿。App ID={_game!.AppId}。",
                detailed: true);
        }
    }

    private async Task<bool> SaveDraftCheckpointAsync(bool recordHistory = true, bool force = false)
    {
        _autosave.Stop();
        await _draftSaveGate.WaitAsync();
        try
        {
            if (_inspection is null) return false;
            if (!HasUnsavedChanges && !force && (!recordHistory || !_draftNeedsRevision)) return true;
            var language = _targetLanguage;
            var snapshot = _inspection.Rows.Select(row => new AchievementEditorRow
            {
                Index = row.Index, ApiName = row.ApiName, Translations = row.Translations,
                TargetName = row.TargetName, TargetDescription = row.TargetDescription,
            }).ToArray();
            if (_project is not null)
            {
                await TranslationProjectService.SaveAsync(_inspection, language, snapshot);
            }
            else
            {
                await _drafts.SaveAsync(_inspection, language, snapshot);
                _draftNeedsRevision = true;
                if (recordHistory)
                {
                    var revision = await _editor.RecordDraftAsync(_inspection, language, snapshot);
                    _draftNeedsRevision = !string.IsNullOrWhiteSpace(revision.RevisionWarning);
                    if (_draftNeedsRevision)
                    {
                        App.ViewModel.ShowInfo($"草稿已保存；{revision.RevisionWarning}", InfoBarSeverity.Warning);
                        await App.Logs.WriteAsync("警告", "成就草稿", "草稿已保存，但修订历史记录未完成。");
                    }
                }
            }
            _editState.Accept(language, snapshot);
            UpdateStatus();
            StatusText.Text += " · 草稿已保存";
            await App.Logs.WriteAsync("信息", "成就草稿", $"草稿已保存；成就={snapshot.Length}。");
            await App.Logs.WriteAsync("详细", "成就草稿",
                $"草稿检查点。App ID={_inspection.AppId}；目标语言={language}。", detailed: true);
            if (HasUnsavedChanges) _autosave.Start();
            return true;
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"草稿保存失败：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteAsync("错误", "成就草稿", $"保存失败；异常类型={exception.GetType().Name}。");
            return false;
        }
        finally { _draftSaveGate.Release(); }
    }

    private async Task<string?> PickDestinationAsync(string format)
    {
        try
        {
            var extension = format == "bin" ? ".bin" : ".zip";
            return NativeFilePickerService.PickSaveFile(
                App.WindowHandle,
                format == "bin" ? "导出 Steam 成就 schema" : "导出投稿 ZIP",
                $"UserGameStatsSchema_{_inspection!.AppId}{extension}",
                format == "bin" ? "Steam 成就 schema" : "投稿 ZIP",
                extension);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法打开保存位置选择器：{exception.Message}", InfoBarSeverity.Error);
            await App.Logs.WriteExceptionDetailsAsync("文件选择器", exception);
            return null;
        }
    }
}
