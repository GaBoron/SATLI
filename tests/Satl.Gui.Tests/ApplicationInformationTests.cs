using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class ApplicationInformationTests
{
    [Fact]
    public void BugReportTargetsCurrentIssueForm()
    {
        Assert.Equal(
            "https://github.com/GaBoron/steam-achievement-translation-installer/issues/new?template=bug_report_zh.yml",
            ApplicationInformation.BugReportUri.AbsoluteUri);
    }

    [Fact]
    public void SupportEmailCopyTextIncludesAddressAndVersionedSubject()
    {
        var text = ApplicationInformation.CreateSupportEmailCopyText("0.13.0");

        Assert.Contains("SATLI.support@proton.me", text);
        Assert.Contains("Steam 成就翻译管理器 v0.13.0 反馈", text);
    }
}
