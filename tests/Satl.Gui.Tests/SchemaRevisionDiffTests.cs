using Satl_Gui.Models;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class SchemaRevisionDiffTests
{
    [Fact]
    public void DiffUsesRemovedAndAddedLinesForChangedFields()
    {
        var previous = Preview(
            Row(0, "ACH_A", "旧名称", "相同说明"),
            Row(1, "ACH_B", "删除名称", "删除说明"));
        var current = Preview(
            Row(0, "ACH_A", "新名称", "相同说明"),
            Row(2, "ACH_C", "新增名称", "新增说明"));

        var lines = new SchemaRevisionDiff(previous, current).LinesFor("schinese");

        Assert.Equal(6, lines.Count);
        Assert.Contains(lines, line =>
            line.ApiName == "ACH_A" && line.Text == "旧名称" && line.Kind == RevisionDiffLineKind.Removed);
        Assert.Contains(lines, line =>
            line.ApiName == "ACH_A" && line.Text == "新名称" && line.Kind == RevisionDiffLineKind.Added);
        Assert.DoesNotContain(lines, line => line.Text == "相同说明");
        Assert.Equal(3, lines.Count(line => line.Kind == RevisionDiffLineKind.Removed));
        Assert.Equal(3, lines.Count(line => line.Kind == RevisionDiffLineKind.Added));
    }

    [Fact]
    public void FirstRevisionTreatsEveryLocalizedFieldAsAdded()
    {
        var diff = new SchemaRevisionDiff(null, Preview(Row(0, "ACH_A", "名称", "说明")));

        var lines = diff.LinesFor("schinese");

        Assert.False(diff.HasParent);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal(RevisionDiffLineKind.Added, line.Kind));
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
