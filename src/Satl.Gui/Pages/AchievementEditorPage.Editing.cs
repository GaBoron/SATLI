using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.Pages;

public sealed partial class AchievementEditorPage
{
    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AchievementEditorRow.TargetName)
            or nameof(AchievementEditorRow.TargetDescription))
        {
            UpdateStatus();
        }
    }

    private void ApplyFilter()
    {
        VisibleRows.Clear();
        if (_inspection is null)
        {
            return;
        }
        foreach (var row in AchievementEditorPresentation.Filter(_inspection.Rows, SearchBox.Text))
        {
            VisibleRows.Add(row);
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        ApplyFilter();

    private void ReferenceLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspection is null || ReferenceLanguageBox.SelectedItem is not string language)
        {
            return;
        }
        foreach (var row in _inspection.Rows)
        {
            row.SelectReference(language);
        }
    }

    private async void TargetLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingLanguage || _inspection is null || TargetLanguageBox.SelectedItem is not string language)
        {
            return;
        }
        await SelectTargetLanguageAsync(language);
    }

    private async void TargetLanguageBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_changingLanguage || _inspection is null)
        {
            return;
        }
        var value = TargetLanguageBox.Text.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(value) && value != _targetLanguage)
        {
            await SelectTargetLanguageAsync(value);
        }
    }

    private async Task SelectTargetLanguageAsync(string language)
    {
        if (_changingLanguage)
        {
            return;
        }
        _changingLanguage = true;
        try
        {
            if (!AchievementEditorPresentation.TryNormalizeLanguage(
                    language,
                    out var normalizedLanguage,
                    out var error))
            {
                App.ViewModel.ShowInfo(error, InfoBarSeverity.Error);
                return;
            }
            if (normalizedLanguage == _targetLanguage)
            {
                return;
            }
            if (HasUnsavedChanges
                && !await ConfirmAsync(
                    "切换目标语言",
                    "当前未保存的修改将被放弃。是否继续？",
                    "继续"))
            {
                TargetLanguageBox.SelectedItem = _targetLanguage;
                TargetLanguageBox.Text = _targetLanguage;
                return;
            }
            _targetLanguage = normalizedLanguage;
            foreach (var row in _inspection!.Rows)
            {
                row.SelectTarget(normalizedLanguage);
            }
            _editState.Accept(_targetLanguage, _inspection.Rows);
            UpdateStatus();
        }
        finally
        {
            _changingLanguage = false;
        }
    }

    private async Task<bool> ConfirmIncompleteAsync(string action)
    {
        if (_inspection is null)
        {
            return false;
        }
        var gaps = AchievementEditorPresentation.CountGaps(_inspection.Rows);
        if (gaps.IsComplete)
        {
            return true;
        }
        return await ConfirmAsync(
            "目标语言内容不完整",
            $"缺少名称 {gaps.MissingNames} 项，缺少说明 {gaps.MissingDescriptions} 项。仍要{action}吗？",
            "继续");
    }

    private void UpdateStatus()
    {
        if (_inspection is null)
        {
            StatusText.Text = "正在加载…";
            return;
        }
        StatusText.Text = AchievementEditorPresentation.BuildStatus(
            _targetLanguage,
            VisibleRows.Count,
            _inspection.Rows,
            HasUnsavedChanges);
    }

    private bool HasUnsavedChanges =>
        _inspection is not null
        && _editState.IsDirty(_targetLanguage, _inspection.Rows);
}
