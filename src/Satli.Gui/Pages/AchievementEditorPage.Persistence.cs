using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.Pages;

public sealed partial class AchievementEditorPage
{
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveChangesAsync();
    }

    private async Task<bool> SaveChangesAsync()
    {
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
                    _drafts.Delete(_inspection.AppId);
                }
                catch (Exception exception)
                {
                    await App.Logs.WriteAsync("警告", "成就草稿", $"本机写回成功，但删除草稿失败：{exception.Message}");
                    await App.Logs.WriteExceptionDetailsAsync("成就草稿", exception);
                }
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
        var draft = _contributions.Prepare(_game!, result);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "投稿到社区",
            Content = $"ZIP 已完成结构、语言字段和 SHA-256 校验。\n\n"
                + $"类型：{(draft.IsUpdate ? "更新已有译本" : "新译本投稿")}\n"
                + $"检测到的语言：{draft.Languages}\n"
                + $"摘要：{draft.Summary}\n\n"
                + "打开表单后，请把资源管理器中选中的 ZIP 拖入附件区域，并由你确认提交。",
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
                $"文件={Path.GetFileName(draft.ZipPath)}。",
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

    private async Task<bool> SaveDraftCheckpointAsync()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }
        if (_inspection is null)
        {
            return false;
        }

        var saved = false;
        await RunBusyAsync(async () =>
        {
            var draft = await _drafts.SaveAsync(
                _inspection,
                _targetLanguage,
                _inspection.Rows);
            var result = await _editor.RecordDraftAsync(
                _inspection,
                _targetLanguage,
                _inspection.Rows);
            _editState.Accept(_targetLanguage, _inspection.Rows);
            UpdateStatus();
            saved = true;
            await App.Logs.WriteAsync(
                "信息",
                "成就草稿",
                $"草稿已保存；包含 {draft.Rows.Count} 个成就。" +
                (string.IsNullOrWhiteSpace(result.RevisionWarning) ? "" : $" {result.RevisionWarning}"));
            await App.Logs.WriteAsync(
                "详细",
                "成就草稿",
                $"草稿检查点已保存。App ID={_inspection.AppId}；目标语言={_targetLanguage}。",
                detailed: true);
            App.ViewModel.ShowInfo(
                string.IsNullOrWhiteSpace(result.RevisionWarning)
                    ? $"已自动保存 {_targetLanguage} 草稿并记录修改历史（{draft.Rows.Count} 个成就）。"
                    : $"已自动保存 {_targetLanguage} 草稿；{result.RevisionWarning}",
                string.IsNullOrWhiteSpace(result.RevisionWarning)
                    ? InfoBarSeverity.Success
                    : InfoBarSeverity.Warning);
        });
        return saved;
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
