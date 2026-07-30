using System.IO.Compression;
using System.Security.Cryptography;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class ContributionWorkflowTests
{
    [Fact]
    public void PrepareBuildsUpdateFormAndValidatesArchive()
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"satl-contribution-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var schema = "schema-payload"u8.ToArray();
            var path = Path.Combine(temporary, "UserGameStatsSchema_123.zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using var stream = archive.CreateEntry("UserGameStatsSchema_123.bin").Open();
                stream.Write(schema);
            }
            var game = new GameItem { AppId = "123", GameName = "Test Game" };
            game.Variants.Add(new SchemaVariantOption
            {
                VariantId = "windows",
                Primary = true,
            });
            game.Variants.Add(new SchemaVariantOption
            {
                VariantId = "linux",
            });
            game.SelectedVariantId = "windows";
            var result = new SchemaEditResult(
                Convert.ToHexString(SHA256.HashData(schema)).ToLowerInvariant(),
                1, 3, 0, 0, false, path, null,
                ChangedNames: 1,
                ChangedDescriptions: 2,
                CompleteLanguages: ["schinese", "english"]);

            var draft = new ContributionWorkflowService().Prepare(game, result);

            Assert.True(draft.IsUpdate);
            Assert.Contains("template=translation_update_zh.yml", draft.IssueFormUri.Query);
            Assert.Contains("variant_id=windows", draft.IssueFormUri.Query);
            Assert.Contains("update_summary=", draft.IssueFormUri.Query);
            Assert.Equal("english, schinese", draft.Languages);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void PrepareRejectsUnexpectedZipStructure()
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"satl-contribution-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "UserGameStatsSchema_123.zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                archive.CreateEntry("nested/UserGameStatsSchema_123.bin");
            }
            var game = new GameItem { AppId = "123", GameName = "Test Game" };
            var result = new SchemaEditResult("unused", 0, 0, 0, 0, false, path, null);

            Assert.Throws<InvalidDataException>(
                () => new ContributionWorkflowService().Prepare(game, result));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
