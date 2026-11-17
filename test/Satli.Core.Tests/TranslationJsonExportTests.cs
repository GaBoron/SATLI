using System.Text;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Xunit;

namespace Satli.Core.Tests;

public sealed class TranslationJsonExportTests
{
    [Fact]
    public void ExportProducesStandaloneTranslationWithAllLanguages()
    {
        var result = TranslationJsonExport.FromBin(Schema(), "570", "reviewed");
        Assert.Equal(2, result["version"]!.GetValue<int>());
        Assert.False(result.ContainsKey("source_sha256"));
        Assert.Equal("reviewed", result["variant_id"]!.GetValue<string>());
        Assert.Equal(new[] { "english", "schinese" }, result["languages"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal("第一步", result["achievements"]!["ACH_FIRST"]!["translations"]!["schinese"]!["name"]!.GetValue<string>());
        var bytes = TranslationJsonExport.Serialize(result);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    public void IncompleteCoverageRejectsExportAndPreservesExistingFile(string missingField)
    {
        var directory = Path.Combine(Path.GetTempPath(), "satli-export-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "UserGameStatsSchema_570.json");
        File.WriteAllText(output, "existing export");
        var error = Assert.Throws<IntegrityException>(() => TranslationJsonExport.Write(Schema(missingField), "570", "default", output));
        Assert.Contains("ACH_FIRST", error.Message);
        Assert.Contains(missingField, error.Message);
        Assert.Contains("schinese", error.Message);
        Assert.Equal("existing export", File.ReadAllText(output));
    }

    [Fact]
    public void UniversallyEmptyDescriptionsAreAllowed()
    {
        var result = TranslationJsonExport.FromBin(Schema(noDescriptions: true), "570", "default");
        Assert.Equal("", result["achievements"]!["ACH_FIRST"]!["translations"]!["english"]!["description"]!.GetValue<string>());
        Assert.Equal("", result["achievements"]!["ACH_FIRST"]!["translations"]!["schinese"]!["description"]!.GetValue<string>());
    }

    private static byte[] Schema(string? missingField = null, bool noDescriptions = false)
    {
        var bits = new BinaryKeyValuesNode(0, "bits");
        var achievement = new BinaryKeyValuesNode(0, "1");
        achievement.Children.Add(Text("name", "ACH_FIRST"));
        var display = new BinaryKeyValuesNode(0, "display");
        var names = new BinaryKeyValuesNode(0, "name");
        names.Children.Add(Text("english", "First step"));
        names.Children.Add(Text("schinese", missingField == "name" ? "" : "第一步"));
        display.Children.Add(names);
        if (!noDescriptions)
        {
            var descriptions = new BinaryKeyValuesNode(0, "desc");
            descriptions.Children.Add(Text("english", "Finish a match."));
            descriptions.Children.Add(Text("schinese", missingField == "description" ? "" : "完成一场比赛。"));
            display.Children.Add(descriptions);
        }
        achievement.Children.Add(display);
        bits.Children.Add(achievement);
        return BinaryKeyValues.Serialize([bits]);
    }

    private static BinaryKeyValuesNode Text(string key, string value) => new(1, key) { RawValue = Encoding.UTF8.GetBytes(value) };
}
