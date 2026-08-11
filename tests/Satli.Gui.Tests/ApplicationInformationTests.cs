using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class ApplicationInformationTests
{
    [Fact]
    public void SponsorPageUsesOfficialAfdianProfile()
    {
        Assert.Equal(
            "https://www.ifdian.net/a/gaboron",
            ApplicationInformation.SponsorUri.AbsoluteUri);
    }

    [Fact]
    public void BugReportTargetsCurrentIssueForm()
    {
        Assert.Equal(
            "https://github.com/GaBoron/SATLI/issues/new?template=bug_report_zh.yml",
            ApplicationInformation.BugReportUri.AbsoluteUri);
    }

    [Fact]
    public void SupportEmailCopyTextIncludesAddressAndVersionedSubject()
    {
        var text = ApplicationInformation.CreateSupportEmailCopyText("0.13.0");

        Assert.Contains("SATLI.support@proton.me", text);
        Assert.Contains("SATLI v0.13.0 反馈", text);
    }
}
