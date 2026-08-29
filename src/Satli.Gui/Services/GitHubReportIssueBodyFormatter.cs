using Satli_Gui.Models;

namespace Satli_Gui.Services;

public static class GitHubReportIssueBodyFormatter
{
    public static string Format(GitHubReportDraft draft)
    {
        var reference = string.IsNullOrWhiteSpace(draft.Reference)
            ? "_No response_"
            : draft.Reference.Trim();
        return $$"""
            ### 游戏名

            {{draft.GameName}}

            ### Steam app ID

            {{draft.AppId}}

            ### 错误类型

            {{draft.ErrorType}}

            ### 错误说明

            {{draft.Reason.Trim()}}

            ### 参考来源

            {{reference}}
            """;
    }
}
