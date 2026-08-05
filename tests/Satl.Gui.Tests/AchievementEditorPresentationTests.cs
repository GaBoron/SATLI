using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl.Gui.Tests;

public sealed class AchievementEditorPresentationTests
{
    [Theory]
    [InlineData(" schinese ", true, "schinese")]
    [InlineData("pt_br", true, "pt_br")]
    [InlineData("TOKEN", false, "token")]
    [InlineData("x", false, "x")]
    [InlineData("zh-CN", false, "zh-cn")]
    public void TryNormalizeLanguage_ValidatesSteamLanguageCodes(
        string value,
        bool expected,
        string normalized)
    {
        var valid = AchievementEditorPresentation.TryNormalizeLanguage(
            value,
            out var language,
            out var error);

        Assert.Equal(expected, valid);
        Assert.Equal(normalized, language);
        Assert.Equal(expected, string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Filter_SearchesApiReferenceAndTargetText()
    {
        var rows = new[]
        {
            Row("ACH_FIRST", "Reference", "Target"),
            Row("ACH_SECOND", "Other", "翻译文本"),
        };

        var filtered = AchievementEditorPresentation.Filter(rows, "翻译");

        Assert.Single(filtered);
        Assert.Equal("ACH_SECOND", filtered[0].ApiName);
    }

    [Fact]
    public void BuildStatus_ReportsGapsVisibilityAndDirtyState()
    {
        var rows = new[]
        {
            Row("ACH_FIRST", "Reference", ""),
            Row("ACH_SECOND", "Other", "Target", targetDescription: ""),
        };

        var status = AchievementEditorPresentation.BuildStatus(
            "schinese", 1, rows, hasUnsavedChanges: true);

        Assert.Contains("显示 1/2", status);
        Assert.Contains("缺少名称 1", status);
        Assert.Contains("缺少说明 1", status);
        Assert.EndsWith("有未保存修改", status);
    }

    private static AchievementEditorRow Row(
        string apiName,
        string referenceName,
        string targetName,
        string targetDescription = "Description") => new()
    {
        ApiName = apiName,
        ReferenceName = referenceName,
        TargetName = targetName,
        TargetDescription = targetDescription,
    };
}
