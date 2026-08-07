namespace Satl_Gui.Services;

internal static class ApplicationInformation
{
    public const string SupportEmailAddress = "SATLI.support@proton.me";
    public const string CopyrightText = "Copyright © 2026 GaBoron";
    public const string SupportedPlatformText = "Windows 10 版本 2004（内部版本 19041）或更高 · x64";

    public static Uri RepositoryUri { get; } = new($"{UpdateService.RepositoryUrl}/");
    public static Uri BugReportUri { get; } = new(
        $"{UpdateService.RepositoryUrl}/issues/new?template=bug_report_zh.yml");
    public static Uri LicenseUri { get; } = new($"{UpdateService.RepositoryUrl}/blob/main/LICENSE");
    public static Uri ThirdPartyNoticesUri { get; } = new(
        $"{UpdateService.RepositoryUrl}/blob/main/THIRD_PARTY_NOTICES.md");

    public static string CreateSupportEmailSubject(string version) =>
        $"Steam 成就翻译管理器 v{version} 反馈";

    public static string CreateSupportEmailCopyText(string version) =>
        $"收件地址：{SupportEmailAddress}{Environment.NewLine}" +
        $"邮件主题：{CreateSupportEmailSubject(version)}";
}
