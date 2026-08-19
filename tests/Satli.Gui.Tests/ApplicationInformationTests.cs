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
    public void SupportEmailUriIncludesAddressAndVersionedSubject()
    {
        var uri = ApplicationInformation.CreateSupportEmailUri("1.1.1");

        Assert.Equal("mailto", uri.Scheme);
        Assert.StartsWith("mailto:SATLI.support@proton.me?", uri.AbsoluteUri);
        Assert.Equal("?subject=SATLI v1.1.1 反馈", Uri.UnescapeDataString(uri.Query));
    }
}
