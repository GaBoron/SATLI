using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class AchievementEditStateTests
{
    [Fact]
    public void AcceptedDraftRemainsCleanAfterRepeatedBindingNotifications()
    {
        var row = Row("原名称", "原说明");
        var state = new AchievementEditState();
        state.Accept("schinese", [row]);

        row.TargetName = "草稿名称";
        Assert.True(state.IsDirty("schinese", [row]));

        state.Accept("schinese", [row]);
        row.TargetName = "草稿名称";
        row.TargetDescription = "原说明";

        Assert.False(state.IsDirty("schinese", [row]));
    }

    [Fact]
    public void RevertingFieldsToAcceptedContentClearsDirtyState()
    {
        var row = Row("原名称", "原说明");
        var state = new AchievementEditState();
        state.Accept("schinese", [row]);

        row.TargetDescription = "修改";
        Assert.True(state.IsDirty("schinese", [row]));

        row.TargetDescription = "原说明";

        Assert.False(state.IsDirty("schinese", [row]));
    }

    private static AchievementEditorRow Row(string name, string description) => new()
    {
        ApiName = "ACH_ONE",
        TargetName = name,
        TargetDescription = description,
    };
}
