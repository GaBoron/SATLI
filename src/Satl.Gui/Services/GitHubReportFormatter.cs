using Satl_Gui.Models;

namespace Satl_Gui.Services;

public static class GitHubReportFormatter
{
    public static string Title(GitHubReportDraft draft) =>
        $"[文件错误] {SingleLine(draft.GameName)} ({draft.AppId})";

    public static string Body(GitHubReportDraft draft) => string.Join(
        Environment.NewLine,
        "### 游戏名",
        "",
        draft.GameName.Trim(),
        "",
        "### Steam app ID",
        "",
        draft.AppId,
        "",
        "### Steam 商店地址",
        "",
        draft.StoreUrl.Trim(),
        "",
        "### 错误类型",
        "",
        draft.ErrorType,
        "",
        "### 错误说明",
        "",
        draft.Reason.Trim(),
        "",
        "### 参考来源",
        "",
        string.IsNullOrWhiteSpace(draft.Reference) ? "_No response_" : draft.Reference.Trim());

    public static void Validate(GitHubReportDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.GameName)
            || !draft.AppId.All(char.IsAsciiDigit)
            || draft.AppId.Length == 0
            || draft.ErrorType is not ("文件可能过期" or "文件可能不生效")
            || string.IsNullOrWhiteSpace(draft.Reason)
            || !Uri.TryCreate(draft.StoreUrl, UriKind.Absolute, out var store)
            || store.Scheme != Uri.UriSchemeHttps
            || !store.Host.Equals("store.steampowered.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("GitHub 报告内容不完整或格式无效。");
        }
    }

    private static string SingleLine(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}
