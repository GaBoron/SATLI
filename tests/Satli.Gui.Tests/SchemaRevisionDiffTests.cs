using Satli_Gui.Models;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class SchemaRevisionDiffTests
{
    [Fact]
    public void DiffKeepsCompleteRowsAndMarksOnlyChangedValues()
    {
        var previous = Preview(
            Row(0, "ACH_A", "旧名称", "相同说明"),
            Row(1, "ACH_B", "删除名称", "删除说明"),
            Row(3, "ACH_D", "未改名称", "未改说明"));
        var current = Preview(
            Row(0, "ACH_A", "新名称", "相同说明"),
            Row(2, "ACH_C", "新增名称", "新增说明"),
            Row(3, "ACH_D", "未改名称", "未改说明"));

        var rows = new SchemaRevisionDiff(previous, current).RowsFor("schinese");

        Assert.Equal(4, rows.Count);
        var changed = Assert.Single(rows, row => row.ApiName == "ACH_A");
        Assert.Equal(RevisionDiffKind.Modified, changed.Name.Kind);
        Assert.Equal("旧名称", changed.Name.Previous);
        Assert.Equal("新名称", changed.Name.Current);
        Assert.Equal(RevisionDiffKind.Unchanged, changed.Description.Kind);

        var removed = Assert.Single(rows, row => row.ApiName == "ACH_B");
        Assert.Equal(RevisionDiffKind.Removed, removed.RowKind);
        Assert.Equal(RevisionDiffKind.Removed, removed.Name.Kind);

        var added = Assert.Single(rows, row => row.ApiName == "ACH_C");
        Assert.Equal(RevisionDiffKind.Added, added.RowKind);
        Assert.Equal(RevisionDiffKind.Added, added.Description.Kind);

        var unchanged = Assert.Single(rows, row => row.ApiName == "ACH_D");
        Assert.Equal(RevisionDiffKind.Unchanged, unchanged.Name.Kind);
        Assert.Equal(RevisionDiffKind.Unchanged, unchanged.Description.Kind);
    }

    [Fact]
    public void FirstRevisionKeepsCompleteContentAsNeutralBaseline()
    {
        var diff = new SchemaRevisionDiff(null, Preview(Row(0, "ACH_A", "名称", "说明")));

        var rows = diff.RowsFor("schinese");

        Assert.False(diff.HasParent);
        var row = Assert.Single(rows);
        Assert.Equal(RevisionDiffKind.Unchanged, row.RowKind);
        Assert.Equal(RevisionDiffKind.Unchanged, row.Name.Kind);
        Assert.Equal(RevisionDiffKind.Unchanged, row.Description.Kind);
    }

    private static ReplacementPreview Preview(params AchievementPreviewRow[] rows) => new(
        "123",
        "Diff Game",
        "default",
        "replace",
        rows.Length,
        ["schinese"],
        rows);

    private static AchievementPreviewRow Row(int index, string id, string name, string description) =>
        new(
            index,
            id,
            new Dictionary<string, AchievementTranslation>(StringComparer.OrdinalIgnoreCase)
            {
                ["schinese"] = new(name, description),
            });
}
