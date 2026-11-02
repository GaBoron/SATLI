using System.Diagnostics;
using System.Security.Cryptography;
using Satli.Core.Formats;
using Satli_Gui.Models;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Satli_Gui.Services;

public sealed record ContributionDraft(
    string AttachmentPath,
    Uri IssueFormUri,
    bool IsUpdate,
    string Languages,
    string Summary);

public sealed class ContributionWorkflowService
{
    public async Task OfferAsync(XamlRoot root, GameItem game, SchemaEditResult result, string? sourceNotes = null)
    {
        try
        {
            await App.Logs.WriteAsync("信息", "翻译投稿", "开始校验已导出的投稿附件。");
            var catalogGame = App.ViewModel.Translations.Games.FirstOrDefault(item => item.AppId == game.AppId) ?? game;
            var draft = await PrepareAsync(catalogGame, result, sourceNotes);
            var attachmentType = Path.GetExtension(draft.AttachmentPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? "JSON" : "ZIP";
            var dialog = new ContentDialog
            {
                XamlRoot = root, Title = "投稿到社区",
                Content = $"{attachmentType} 已完成结构、语言字段和完整性校验。\n\n"
                    + $"类型：{(draft.IsUpdate ? "更新已有译本" : "新译本投稿")}\n"
                    + $"检测到的语言：{draft.Languages}\n摘要：{draft.Summary}\n\n"
                    + $"打开表单后，请把资源管理器中选中的 {attachmentType} 拖入附件区域，并由你确认提交。",
                PrimaryButtonText = "打开投稿表单", CloseButtonText = "稍后投稿", DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await OpenAsync(draft);
                await App.Logs.WriteAsync("信息", "翻译投稿", "已打开预填的 GitHub 投稿表单。");
            }
            else await App.Logs.WriteAsync("信息", "翻译投稿", "用户稍后投稿；已导出附件保留。");
            await App.Logs.WriteAsync("详细", "翻译投稿",
                $"投稿准备完成。App ID={game.AppId}；类型={(draft.IsUpdate ? "更新" : "新投稿")}；语言={draft.Languages}；"
                + $"文件={Path.GetFileName(draft.AttachmentPath)}。", detailed: true);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"文件已导出，但投稿准备未完成：{exception.Message}", InfoBarSeverity.Warning);
            await App.Logs.WriteAsync("警告", "翻译投稿", "附件导出成功，但投稿准备或打开表单失败。");
            await App.Logs.WriteAsync("详细", "翻译投稿", $"异常类型={exception.GetType().Name}。", detailed: true);
        }
    }

    public Task<ContributionDraft> PrepareAsync(GameItem game, SchemaEditResult result, string? sourceNotes = null) =>
        Task.Run(() => Prepare(game, result, sourceNotes));

    public ContributionDraft Prepare(GameItem game, SchemaEditResult result, string? sourceNotes = null)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidDataException("投稿附件路径为空。");
        }
        var attachmentPath = Path.GetFullPath(result.Output);
        var isJson = Path.GetExtension(attachmentPath).Equals(".json", StringComparison.OrdinalIgnoreCase);
        var expectedName = $"UserGameStatsSchema_{game.AppId}.{(isJson ? "json" : "zip")}";
        if (!Path.GetFileName(attachmentPath).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"投稿文件必须命名为 {expectedName}。");
        }
        if (!File.Exists(attachmentPath))
        {
            throw new FileNotFoundException("找不到刚导出的投稿附件。", attachmentPath);
        }

        if (isJson && new FileInfo(attachmentPath).Length > TranslationJson.MaximumBytes)
            throw new InvalidDataException("投稿 JSON 超过 32 MiB 限制。");
        var schema = isJson ? File.ReadAllBytes(attachmentPath)
            : SchemaZipArchive.ReadSingleSchema(attachmentPath, game.AppId);
        var schemaSha256 = Convert.ToHexString(SHA256.HashData(schema)).ToLowerInvariant();
        if (!schemaSha256.Equals(result.OutputSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("投稿附件内容与导出结果不一致。");
        }
        var json = isJson ? TranslationJson.Parse(schema, game.AppId) : null;
        if (json is not null && json["version"]!.GetValue<int>() != 2)
            throw new InvalidDataException("独立投稿必须使用格式 2 JSON。");
        var languages = (json is not null
                ? json["languages"]!.AsArray().Select(item => item!.GetValue<string>())
                : result.SubmissionLanguages ?? result.CompleteLanguages ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (languages.Length == 0)
        {
            throw new InvalidDataException("投稿文件没有任何可识别的语言字段。");
        }

        var isUpdate = game.Variants.Count > 0;
        var summary = result.ChangedNames == 0 && result.ChangedDescriptions == 0
            ? "重新导出并校验现有译本。"
            : $"修正 {result.ChangedNames} 项成就名称和 {result.ChangedDescriptions} 项成就说明。";
        var template = isUpdate
            ? "translation_update_zh.yml"
            : "translation_contribution_zh.yml";
        var title = $"[{(isUpdate ? "翻译更新" : "翻译投稿")}] {game.GameName} ({game.AppId})";
        var fields = new Dictionary<string, string?>
        {
            ["game_name"] = game.GameName,
            ["app_id"] = game.AppId,
            ["notes"] = sourceNotes ?? "由 SATLI 准备，请人工审校后提交。",
        };
        if (isUpdate)
        {
            fields["update_summary"] = summary;
            if (json is not null)
            {
                fields["variant_id"] = json["variant_id"]!.GetValue<string>();
            }
            else if (game.Variants.Count > 1 && !string.IsNullOrWhiteSpace(game.SelectedVariantId))
            {
                fields["variant_id"] = game.SelectedVariantId;
            }
        }
        return new ContributionDraft(
            attachmentPath,
            GitHubIssueFormUriBuilder.Build(template, title, fields),
            isUpdate,
            string.Join(", ", languages),
            summary);
    }

    public async Task OpenAsync(ContributionDraft draft)
    {
        if (!await Launcher.LaunchUriAsync(draft.IssueFormUri))
        {
            throw new InvalidOperationException("系统未能打开 GitHub 投稿表单。");
        }
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"/select,{draft.AttachmentPath}");
        Process.Start(startInfo);
    }
}
