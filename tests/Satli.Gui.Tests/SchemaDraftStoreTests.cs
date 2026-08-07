using Satli_Gui.Models;
using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class SchemaDraftStoreTests
{
    [Fact]
    public async Task DraftRoundTripsWithoutTouchingSchema()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"satli-draft-{Guid.NewGuid():N}");
        try
        {
            var store = new SchemaDraftStore(directory);
            var inspection = Inspection("source-hash");
            inspection.Rows[0].TargetName = "草稿名称";
            inspection.Rows[0].TargetDescription = "草稿说明";

            await store.SaveAsync(inspection, "schinese", inspection.Rows);
            var loaded = await store.LoadAsync("123");

            Assert.NotNull(loaded);
            Assert.Equal("schinese", loaded.TargetLanguage);
            Assert.Equal("草稿名称", loaded.Rows[0].Name);
            Assert.Equal("草稿说明", loaded.Rows[0].Description);
            Assert.Null(SchemaDraftStore.CompatibilityError(loaded, inspection));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task DraftRejectsStaleSchemaAndUnsafeText()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"satli-draft-{Guid.NewGuid():N}");
        try
        {
            var store = new SchemaDraftStore(directory);
            var inspection = Inspection("old-hash");
            await store.SaveAsync(inspection, "schinese", inspection.Rows);
            var draft = await store.LoadAsync("123");

            Assert.Contains("已变化", SchemaDraftStore.CompatibilityError(
                draft!,
                Inspection("new-hash")));
            inspection.Rows[0].TargetName = "bad\ntext";
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.SaveAsync(inspection, "schinese", inspection.Rows));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static SchemaInspection Inspection(string sourceHash)
    {
        var row = new AchievementEditorRow
        {
            ApiName = "ACH_ONE",
            Translations = new Dictionary<string, EditorTranslation>
            {
                ["schinese"] = new("原名称", "原说明"),
            },
        };
        row.SelectTarget("schinese");
        return new SchemaInspection(
            "123",
            @"C:\Steam\UserGameStatsSchema_123.bin",
            sourceHash,
            false,
            ["schinese"],
            [row]);
    }
}
