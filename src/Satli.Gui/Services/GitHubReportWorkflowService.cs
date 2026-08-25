using Satli_Gui.Models;
using Windows.System;

namespace Satli_Gui.Services;

public sealed class GitHubReportWorkflowService
{
    public Uri Prepare(GitHubReportDraft draft)
    {
        Validate(draft);
        return GitHubIssueFormUriBuilder.Build(
            "outdated_report_zh.yml",
            $"[文件错误] {SingleLine(draft.GameName)} ({draft.AppId})",
            new Dictionary<string, string?>
            {
                ["game_name"] = SingleLine(draft.GameName),
                ["app_id"] = draft.AppId,
                ["error_type"] = draft.ErrorType,
                ["reason"] = draft.Reason.Trim(),
                ["reference"] = draft.Reference.Trim(),
            });
    }

    public async Task OpenAsync(Uri issueFormUri)
    {
        if (!await Launcher.LaunchUriAsync(issueFormUri))
        {
            throw new InvalidOperationException("系统未能打开 GitHub 文件错误报告表单。");
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
