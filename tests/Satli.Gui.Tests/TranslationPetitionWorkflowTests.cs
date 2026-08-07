using System.IO.Compression;
using Satli_Gui.Models;
using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class TranslationPetitionWorkflowTests
{
    [Fact]
    public void PrepareBuildsPrefilledIssueFormForValidatedSchema()
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"satli-petition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "UserGameStatsSchema_123.zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using var stream = archive.CreateEntry("UserGameStatsSchema_123.bin").Open();
                stream.Write("schema-payload"u8);
            }

            var draft = new TranslationPetitionWorkflowService().Prepare(
                new TranslationPetitionInput(
                    "Test Game",
                    "123",
                    "schinese， tchinese;schinese",
                    "请优先统一角色名。"),
                path);

            var query = Uri.UnescapeDataString(draft.IssueFormUri.Query);
            Assert.Contains("template=translation_petition_zh.yml", query);
            Assert.Contains("title=[翻译请愿] Test Game (123)", query);
            Assert.Contains("game_name=Test Game", query);
            Assert.Contains("app_id=123", query);
            Assert.Contains("target_languages=schinese, tchinese", query);
            Assert.Contains("schema SHA-256", query);
            Assert.DoesNotContain("schema_zip=", query);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Theory]
    [InlineData("", "123", "schinese")]
    [InlineData("Test Game", "abc", "schinese")]
    [InlineData("Test Game", "123", "简体中文")]
    public void NormalizeInputRejectsInvalidIssueFields(
        string gameName,
        string appId,
        string languages)
    {
        Assert.Throws<ArgumentException>(() =>
            new TranslationPetitionWorkflowService().NormalizeInput(
                new TranslationPetitionInput(gameName, appId, languages, string.Empty)));
    }

    [Fact]
    public void LocalGameCanRequestTranslationOnlyWhenChineseAndCatalogTranslationAreMissing()
    {
        var missingChinese = new GameItem
        {
            AppId = "123",
            GameName = "English Only",
            NativeLanguages = ["english", "japanese"],
        };
        var nativeChinese = new GameItem
        {
            AppId = "124",
            GameName = "Chinese Game",
            NativeLanguages = ["english", "schinese"],
        };
        var translated = new GameItem
        {
            AppId = "125",
            GameName = "Catalog Game",
            NativeLanguages = ["english"],
        };
        translated.Variants.Add(new SchemaVariantOption
        {
            VariantId = "default",
            Primary = true,
        });
        var unreadable = new GameItem
        {
            AppId = "126",
            GameName = "Unreadable",
        };

        Assert.True(missingChinese.CanRequestTranslation);
        Assert.False(nativeChinese.CanRequestTranslation);
        Assert.False(translated.CanRequestTranslation);
        Assert.False(unreadable.CanRequestTranslation);
    }
}
