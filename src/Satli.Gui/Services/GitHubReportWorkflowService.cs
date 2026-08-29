using Satli_Gui.Models;
using Windows.System;

namespace Satli_Gui.Services;

public sealed class GitHubReportWorkflowService
{
    public Uri Prepare(GitHubReportDraft draft)
    {
        Validate(draft);
        var normalized = draft with { GameName = SingleLine(draft.GameName) };
        return GitHubIssueUriBuilder.Build(
        [
            new("title", $"[文件错误] {normalized.GameName} ({normalized.AppId})"),
            new("body", GitHubReportIssueBodyFormatter.Format(normalized)),
        ]);
    }

    public async Task OpenAsync(Uri issueFormUri)
    {
        if (!await Launcher.LaunchUriAsync(issueFormUri))
        {
            throw new InvalidOperationException("系统未能打开 GitHub 文件错误报告草稿。");
        }
    }

    public static void Validate(GitHubReportDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.GameName)
            || !draft.AppId.All(char.IsAsciiDigit)
            || draft.AppId.Length == 0
            || draft.ErrorType is not ("文件可能过期" or "文件可能不生效")
            || string.IsNullOrWhiteSpace(draft.Reason))
        {
            throw new ArgumentException("GitHub 报告内容不完整或格式无效。");
        }
    }

    private static string SingleLine(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}
