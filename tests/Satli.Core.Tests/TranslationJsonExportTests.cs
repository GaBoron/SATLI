using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Satli.Cli;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Satli.Core.SchemaEditing;
using Xunit;

namespace Satli.Core.Tests;

public sealed class TranslationJsonExportTests
{
    [Fact]
    public void ExportIsDeterministicAcrossNodeOrderAndRetainsAllLanguagesWithoutBinHash()
    {
        using var box = new ExportSandbox();
        var first = TranslationJsonExport.FromBin(Schema(), "570", "reviewed");
        var second = TranslationJsonExport.FromBin(Schema(reverse: true), "570", "reviewed");
        var bytes = TranslationJsonExport.Serialize(first);
        Assert.Equal(bytes, TranslationJsonExport.Serialize(second));
        Assert.Equal(5, first.Count);
        Assert.Equal(2, first["version"]!.GetValue<int>());
        Assert.False(first.ContainsKey("source_sha256"));
        Assert.Equal(new[] { "english", "schinese" }, first["languages"]!.AsArray().Select(item => item!.ToString()));
        Assert.Equal("第一步", first["achievements"]!["ACH_FIRST"]!["translations"]!["schinese"]!["name"]!.GetValue<string>());
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        TranslationJsonExport.Write(Schema(), "570", "reviewed", box.Output);
        Assert.Equal(bytes, File.ReadAllBytes(box.Output));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    public void IncompleteCoverageReportsApiFieldAndLanguageAndPreservesExistingOutput(string field)
    {
        using var box = new ExportSandbox();
        File.WriteAllText(box.Output, "existing export");
        var exception = Assert.Throws<IntegrityException>(() => TranslationJsonExport.Write(
            Schema(missingField: field), "570", "default", box.Output));
        Assert.Contains("ACH_FIRST", exception.Message);
        Assert.Contains(field, exception.Message);
        Assert.Contains("schinese", exception.Message);
        Assert.Equal("existing export", File.ReadAllText(box.Output));
    }

    [Fact]
    public void MissingDescriptionNodesAndUniversallyEmptyFieldsAreAllowed()
    {
        var translation = TranslationJsonExport.FromBin(Schema(noDescriptions: true), "570", "default");
        foreach (var achievement in translation["achievements"]!.AsObject())
            foreach (var language in achievement.Value!["translations"]!.AsObject())
                Assert.Equal("", language.Value!["description"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Invalid Variant")]
    [InlineData("-bad")]
    [InlineData("")]
    public void ExplicitInvalidVariantIsRejectedInsteadOfSilentlyReplaced(string variant) =>
        Assert.Throws<IntegrityException>(() => TranslationJsonExport.FromBin(Schema(), "570", variant));

    [Theory]
    [InlineData("0")]
    [InlineData("4294967296")]
    [InlineData("0570")]
    public void InvalidAppIdsAreRejected(string appId) =>
        Assert.Throws<IntegrityException>(() => TranslationJsonExport.FromBin(Schema(), appId, "default"));

    [Fact]
    public void DuplicateApiNamesAndInvalidOutputNamesAreRejected()
    {
        using var box = new ExportSandbox();
        var nodes = BinaryKeyValues.Parse(Schema());
        nodes[0].Children.Add(nodes[0].Children[0]);
        Assert.Throws<IntegrityException>(() => TranslationJsonExport.FromBin(BinaryKeyValues.Serialize(nodes), "570", "default"));
        Assert.Throws<UsageException>(() => TranslationJsonExport.Write(Schema(), "570", "default", Path.Combine(box.Root, "wrong.json")));
    }

    [Fact]
    public void FormatTwoTextLimitIsMeasuredInCharacters()
    {
        var nodes = BinaryKeyValues.Parse(Schema());
        var names = BinaryKeyValues.NestedObject(nodes[0].Children[0], "display", "name")!;
        names.Children[0].RawValue = Encoding.UTF8.GetBytes(new string('译', 65536));
        TranslationJsonExport.FromBin(BinaryKeyValues.Serialize(nodes), "570", "default");
        names.Children[0].RawValue = Encoding.UTF8.GetBytes(new string('译', 65537));
        Assert.Throws<IntegrityException>(() => TranslationJsonExport.FromBin(BinaryKeyValues.Serialize(nodes), "570", "default"));
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("zip")]
    public async Task CliConvertsSourceWithoutSteamOrStateWrites(string format)
    {
        using var box = new ExportSandbox();
        var source = Path.Combine(box.Root, $"UserGameStatsSchema_570.{format}");
        if (format == "bin") File.WriteAllBytes(source, Schema());
        else SchemaEditor.WriteZip(source, "UserGameStatsSchema_570.bin", Schema());
        var before = File.ReadAllBytes(source);
        var result = await box.Run("schema", "export", "570", "--source", source, "--format", "json",
            "--output", box.Output, "--variant-id", "imported", "--steam-dir", Path.Combine(box.Root, "no-steam"));
        Assert.Equal(0, result.Code);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.False(Directory.Exists(box.Data));
        Assert.Equal("imported", TranslationJson.Parse(File.ReadAllBytes(box.Output), "570")["variant_id"]!.GetValue<string>());
        Assert.DoesNotContain("revision_commit", result.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CliDraftExportChecksSourceAndNeverWritesSteamBackupsOrRevisions(bool stale)
    {
        using var box = new ExportSandbox();
        var steam = Path.Combine(box.Root, "Steam");
        Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats"));
        File.WriteAllText(Path.Combine(steam, "steam.exe"), "");
        var source = Path.Combine(steam, "appcache", "stats", "UserGameStatsSchema_570.bin");
        var original = Schema();
        File.WriteAllBytes(source, original);
        var edits = new JsonObject
        {
            ["version"] = 1, ["app_id"] = "570", ["target_language"] = "schinese",
            ["source_sha256"] = stale ? new string('0', 64) : FileOperations.Sha256(original),
            ["rows"] = new JsonArray(new[] { "ACH_FIRST", "ACH_SECOND" }.Select(api => (JsonNode)new JsonObject
            {
                ["api_name"] = api, ["name"] = "草稿名称", ["description"] = "草稿说明",
            }).ToArray()),
        };
        var editsPath = Path.Combine(box.Root, "edits.json");
        File.WriteAllText(editsPath, edits.ToJsonString());
        var result = await box.Run("schema", "export", "570", "--steam-dir", steam,
            "--format", "json", "--output", box.Output, "--edits-file", editsPath, "--target-language", "schinese");
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.False(Directory.Exists(box.Data));
        Assert.Equal(!stale, File.Exists(box.Output));
        if (stale) Assert.NotEqual(0, result.Code);
        else
        {
            Assert.Equal(0, result.Code);
            var translation = TranslationJson.Parse(File.ReadAllBytes(box.Output), "570");
            Assert.Equal("草稿名称", translation["achievements"]!["ACH_FIRST"]!["translations"]!["schinese"]!["name"]!.GetValue<string>());
            Assert.Equal("First step", translation["achievements"]!["ACH_FIRST"]!["translations"]!["english"]!["name"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task IncompleteFlagCannotBypassSubmissionValidation()
    {
        using var box = new ExportSandbox();
        var source = Path.Combine(box.Root, "UserGameStatsSchema_570.bin");
        File.WriteAllBytes(source, Schema(missingField: "description"));
        var result = await box.Run("schema", "export", "570", "--source", source, "--format", "json",
            "--output", box.Output, "--allow-incomplete");
        Assert.NotEqual(0, result.Code);
        Assert.Contains("schinese", result.Events);
        Assert.False(File.Exists(box.Output));
        Assert.False(Directory.Exists(box.Data));
    }

    [Fact]
    public async Task ManagedExportUsesTrustedSnapshotWhenSteamHasReplacedTheTranslation()
    {
        using var box = new ExportSandbox();
        var steam = Path.Combine(box.Root, "Steam");
        Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats"));
        File.WriteAllText(Path.Combine(steam, "steam.exe"), "");
        var source = Path.Combine(steam, "appcache", "stats", "UserGameStatsSchema_570.bin");
        File.WriteAllBytes(source, Schema(reverse: true));
        new SchemaEditor().Apply(source, "570", Schema(), box.Data, "Example", "schinese");
        File.WriteAllBytes(source, Schema(missingField: "name"));
        var sourceBefore = File.ReadAllBytes(source);
        var dataBefore = Snapshot(box.Data);
        var result = await box.Run("schema", "export", "570", "--managed", "--steam-dir", steam,
            "--format", "json", "--output", box.Output);
        Assert.Equal(0, result.Code);
        Assert.Equal(dataBefore, Snapshot(box.Data));
        Assert.Equal(sourceBefore, File.ReadAllBytes(source));
        Assert.Equal("第一步", TranslationJson.Parse(File.ReadAllBytes(box.Output), "570")
            ["achievements"]!["ACH_FIRST"]!["translations"]!["schinese"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task JsonHistoryExportDoesNotImportLegacyRecords()
    {
        using var box = new ExportSandbox();
        var head = Path.Combine(box.Data, "schema-revisions.git", "refs", "heads", "main");
        Directory.CreateDirectory(Path.GetDirectoryName(head)!);
        File.WriteAllText(head, new string('a', 40));
        var before = Snapshot(box.Data);
        var result = await box.Run("schema", "revisions", "export", "570", "aaaa",
            "--format", "json", "--output", box.Output);
        Assert.NotEqual(0, result.Code);
        var error = result.Events.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!)
            .Single(item => item["event"]!.GetValue<string>() == "error")["payload"]!["message"]!.GetValue<string>();
        Assert.Contains("找不到修订", error);
        Assert.DoesNotContain("无法导入旧版", error);
        Assert.Equal(before, Snapshot(box.Data));
        Assert.False(File.Exists(box.Output));
    }

    [Fact]
    public async Task RevisionJsonExportUsesSelectedVariantAndKeepsHistoryAndBinZipExports()
    {
        using var box = new ExportSandbox();
        var store = new SchemaRevisionStore(box.Data);
        var revision = store.Record("570", Schema(), "draft", variantId: "recorded");
        var before = Snapshot(box.Data);
        var result = await box.Run("schema", "revisions", "export", "570", revision.Commit,
            "--format", "json", "--output", box.Output, "--variant-id", "reviewed");
        Assert.Equal(0, result.Code);
        Assert.Equal("reviewed", TranslationJson.Parse(File.ReadAllBytes(box.Output), "570")["variant_id"]!.GetValue<string>());
        Assert.Equal(before, Snapshot(box.Data));
        var bin = Path.Combine(box.Root, "export.bin");
        var zip = Path.Combine(box.Root, "export.zip");
        store.Export("570", revision.Commit, bin, "bin");
        store.Export("570", revision.Commit, zip, "zip");
        Assert.Equal(Schema(), File.ReadAllBytes(bin));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal("UserGameStatsSchema_570.bin", Assert.Single(archive.Entries).FullName);
    }

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToBase64String(File.ReadAllBytes(path))).ToArray();

    private static byte[] Schema(bool reverse = false, string? missingField = null, bool noDescriptions = false)
    {
        var bits = new BinaryKeyValuesNode(0, "bits");
        foreach (var api in new[] { "ACH_FIRST", "ACH_SECOND" })
        {
            var achievement = new BinaryKeyValuesNode(0, api);
            achievement.Children.Add(Text("name", api));
            var display = new BinaryKeyValuesNode(0, "display");
            var names = new BinaryKeyValuesNode(0, "name");
            names.Children.Add(Text("english", "First step"));
            names.Children.Add(Text("Schinese", missingField == "name" ? "" : "第一步"));
            if (reverse) names.Children.Reverse();
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
        }
        if (reverse) bits.Children.Reverse();
        return BinaryKeyValues.Serialize([bits]);
    }

    private static BinaryKeyValuesNode Text(string key, string value) => new(1, key) { RawValue = Encoding.UTF8.GetBytes(value) };

    private sealed class ExportSandbox : IDisposable
    {
        private static readonly string Parent = Path.Combine(Path.GetTempPath(), "satli-json-export-tests");
        public string Root { get; } = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        public string Data => Path.Combine(Root, "data");
        public string Output => Path.Combine(Root, "UserGameStatsSchema_570.json");
        public ExportSandbox() => Directory.CreateDirectory(Root);
        public async Task<(int Code, string Events)> Run(params string[] arguments)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await CliApplication.RunAsync([.. arguments, "--data-dir", Data, "--jsonl"], output, error);
            return (code, output.ToString() + error.ToString());
        }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected test directory");
            RecycleBin.DirectoryIfExists(resolved);
        }
    }
}
