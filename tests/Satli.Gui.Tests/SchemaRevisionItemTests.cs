using System.Text.Json;
using Satli_Gui.Models;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class SchemaRevisionItemTests
{
    [Fact]
    public void RevisionTitleUsesLocalCommitTimeAndDraftAction()
    {
        using var document = JsonDocument.Parse(
            """{"created_at":"2026-07-30T10:01:00Z","action":"draft","target_language":"schinese","achievement_count":10,"parent_schema_sha256":"parent-hash"}""");

        var item = SchemaRevisionItem.FromPayload(document.RootElement);
        var expected = DateTimeOffset.Parse("2026-07-30T10:01:00Z")
            .ToLocalTime()
            .ToString("yyyy年M月d日 HH:mm:ss");

        Assert.Equal(expected, item.TitleText);
        Assert.Equal("自动保存草稿 · schinese · 10 个成就", item.SummaryText);
        Assert.Equal("parent-hash", item.ParentSchemaSha256);
    }
}
