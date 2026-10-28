using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.Formats;
using Satli.Core.SatliLite;
using Satli.Core.Imports;
using System.IO.Compression;
using Xunit;

namespace Satli.Core.Tests;

public sealed class LiteTranslationTests
{
    [Fact]
    public void ActualBinConversionPreservesAchievementsWithNoDescriptionNode()
    {
        var payload = BinWithLanguages("Schinese");
        var translation = LiteTranslation.FromBin(payload, "570");
        Assert.Single(translation["achievements"]!.AsObject());
        Assert.Equal("第一步", translation["achievements"]!["ACH_FIRST"]!["translations"]!["schinese"]!["name"]!.GetValue<string>());
        Assert.Equal("", translation["achievements"]!["ACH_FIRST"]!["translations"]!["schinese"]!["description"]!.GetValue<string>());
    }

    [Fact]
    public void ActualBinConversionRejectsCaseCollidingLanguageKeys() =>
        Assert.Throws<IntegrityException>(() => LiteTranslation.FromBin(BinWithLanguages("schinese", "Schinese"), "570"));

    [Theory]
    [InlineData("bin")]
    [InlineData("zip")]
    public void LocalFilesWithMissingDescriptionCanBeConvertedWithoutRelaxingBinInstallValidation(string extension)
    {
        using var box = new LiteSandbox();
        var path = Path.Combine(box.Root, $"UserGameStatsSchema_570.{extension}");
        var payload = BinWithLanguages("schinese");
        if (extension == "zip")
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var stream = zip.CreateEntry("UserGameStatsSchema_570.bin").Open();
            stream.Write(payload);
        }
        else File.WriteAllBytes(path, payload);
        Assert.Throws<PreflightException>(() => LocalImportReader.Read(path));
        var artifact = LocalImportReader.Read(path, requireAchievements: false);
        var translation = LiteTranslation.FromBin(artifact.Payload, artifact.AppId);
        Assert.Single(translation["achievements"]!.AsObject());
    }

    private static byte[] BinWithLanguages(params string[] languages)
    {
        var bits = new BinaryKeyValuesNode(0, "bits");
        var achievement = new BinaryKeyValuesNode(0, "0");
        achievement.Children.Add(new BinaryKeyValuesNode(1, "name") { RawValue = Encoding.UTF8.GetBytes("ACH_FIRST") });
        var display = new BinaryKeyValuesNode(0, "display");
        var names = new BinaryKeyValuesNode(0, "name");
        foreach (var language in languages) names.Children.Add(new BinaryKeyValuesNode(1, language) { RawValue = Encoding.UTF8.GetBytes("第一步") });
        display.Children.Add(names);
        achievement.Children.Add(display);
        bits.Children.Add(achievement);
        return BinaryKeyValues.Serialize([bits]);
    }

    [Fact]
    public void BinConversionKeepsApiNamesAndNormalizesLanguageKeysWithoutInventingText()
    {
        var row = new AchievementPreviewRow(1, "ACH_FIRST", new Dictionary<string, AchievementTranslation>
        {
            ["Schinese"] = new("第一步", ""), ["english"] = new("First step", "Finish a match."),
        });
        var translation = LiteTranslation.FromRows("570", "local-edit-1", [row], new string('a', 64));
        Assert.Equal(new[] { "english", "schinese" }, translation["languages"]!.AsArray().Select(value => value!.ToString()));
        Assert.Equal("", translation["achievements"]!["ACH_FIRST"]!["translations"]!["schinese"]!["description"]!.GetValue<string>());
        Assert.Equal(1, translation["version"]!.GetValue<int>());
    }

    [Fact]
    public void EditorConversionHasNoInventedBinHash()
    {
        var translation = LiteSandbox.Translation();
        Assert.Equal(2, translation["version"]!.GetValue<int>());
        Assert.False(translation.ContainsKey("source_sha256"));
        Assert.Equal(5, translation.Count);
    }

    [Fact]
    public void DuplicateApiNamesAndAmbiguousLanguageMappingAreRejected()
    {
        var row = new AchievementPreviewRow(1, "ACH_FIRST", new Dictionary<string, AchievementTranslation> { ["schinese"] = new("名称", "描述") });
        Assert.Throws<IntegrityException>(() => LiteTranslation.FromRows("570", "local", [row, row]));
        var ambiguous = row with { Translations = new Dictionary<string, AchievementTranslation>
        {
            ["Schinese"] = new("名称", "描述"), ["schinese"] = new("名称2", "描述2"),
        } };
        Assert.Throws<IntegrityException>(() => LiteTranslation.FromRows("570", "local", [ambiguous]));
    }

    [Theory]
    [InlineData("{\"version\":2,\"version\":2}")]
    [InlineData("{\"version\":2,\"app_id\":\"0\"}")]
    public void MalformedAndDuplicateJsonFieldsAreRejected(string text) =>
        Assert.Throws<IntegrityException>(() => TranslationJson.Parse(Encoding.UTF8.GetBytes(text), "570"));

    [Fact]
    public void FormatTwoRejectsInconsistentLanguageCoverage()
    {
        var translation = LiteSandbox.Translation();
        translation["languages"] = new JsonArray("english", "schinese");
        translation["achievements"]!["ACH_FIRST"]!["translations"]!["english"] = new JsonObject { ["name"] = "", ["description"] = "" };
        Assert.Throws<IntegrityException>(() => TranslationJson.Parse(JsonSerializer.SerializeToUtf8Bytes(translation), "570"));
    }
}
