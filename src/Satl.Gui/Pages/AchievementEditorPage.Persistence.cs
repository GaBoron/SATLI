using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.Pages;

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
                "请先从系统托盘正常退出 Steam。应用会在写回前创建可恢复备份。",
                "确认写回"))
        {
            return false;
        }
        var saved = false;
        await RunBusyAsync(async () =>
        {
            var result = await _editor.ApplyAsync(
                _inspection!, _targetLanguage, _inspection!.Rows, allowIncomplete: true);
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
            Content = $"ZIP 已完成结构、语言和 SHA-256 校验。\n\n"
                + $"类型：{(draft.IsUpdate ? "更新已有译本" : "新译本投稿")}\n"
                + $"完整语言：{draft.Languages}\n"
                + $"摘要：{draft.Summary}\n\n"
                + "打开表单后，请把资源管理器中选中的 ZIP 拖入附件区域，并由你确认提交。",
            PrimaryButtonText = "打开投稿表单",
            CloseButtonText = "稍后投稿",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _contributions.OpenAsync(draft);
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
