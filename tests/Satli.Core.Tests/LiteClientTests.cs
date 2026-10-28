using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Cli;
using Satli.Core.FileSystem;
using Satli.Core.SatliLite;
using Xunit;

namespace Satli.Core.Tests;

public sealed class LiteClientTests
{
    [Fact]
    public async Task LegacyMigrationReadsOnlyIdentifiersAndNeverChangesTheOldSnapshot()
    {
        using var box = new LiteSandbox();
        var oldPlugin = Path.Combine(box.Steam, "millennium/plugins/satli.star");
        File.WriteAllText(oldPlugin, "legacy package");
        var snapshot = Path.Combine(box.Steam, "millennium/config/satli-bridge-v1.json");
        const string content = "{\"version\":1,\"apps\":{\"570\":{\"achievements\":{}}}}";
        File.WriteAllText(snapshot, content);
        Assert.Equal("legacy_bridge", (await box.Client.InspectAsync()).State);
        Assert.Equal(new[] { "570" }, await box.Client.LegacyAppIdsAsync());
        await Assert.ThrowsAsync<PreflightException>(() => box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited"));
        Assert.Equal(content, File.ReadAllText(snapshot));
        Assert.True(File.Exists(oldPlugin));
    }

    [Theory]
    [InlineData(false, false, "millennium_missing", false)]
    [InlineData(true, false, "not_installed", false)]
    [InlineData(true, true, "steam_stopped", true)]
    public async Task DiscoveryRequiresAnInstalledCompatiblePlugin(bool millennium, bool plugin, string state, bool canSubmit)
    {
        using var box = new LiteSandbox(millennium, plugin);
        var connection = await box.Client.InspectAsync();
        Assert.Equal(state, connection.State);
        Assert.Equal(canSubmit, connection.CanSubmit);
    }

    [Fact]
    public async Task ACompatibleOfflineImportQueuesWithoutAnyBinInstallation()
    {
        using var box = new LiteSandbox();
        var result = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "catalog_json");
        Assert.Equal("submitted", result.State);
        Assert.True(result.Pending);
        Assert.False(result.Succeeded);
        var request = box.Request(result);
        Assert.Equal("reject", request["conflict"]!["mode"]!.GetValue<string>());
        Assert.Equal("catalog_json", request["source"]!["kind"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(box.Steam, "appcache")));
        Assert.False(File.Exists(Path.Combine(box.Steam, "millennium/config/satli-lite/state.json")));
    }

    [Fact]
    public async Task AnUninitializedOrIncompatiblePluginCannotReceiveAnImport()
    {
        using var box = new LiteSandbox();
        File.Move(Path.Combine(box.Mailbox, "protocol.json"), Path.Combine(box.Mailbox, "saved-protocol"));
        Assert.False((await box.Client.InspectAsync()).CanSubmit);
        await Assert.ThrowsAsync<PreflightException>(() => box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited"));
        box.Protocol["protocol"] = 2;
        box.Write("protocol.json", box.Protocol);
        Assert.Equal("incompatible", (await box.Client.InspectAsync()).State);
        Assert.False(Directory.Exists(Path.Combine(box.Mailbox, "inbox")));
    }

    [Theory]
    [InlineData(-60, true, "unavailable")]
    [InlineData(60, true, "unavailable")]
    [InlineData(0, false, "unavailable")]
    [InlineData(0, true, "connected")]
    public async Task OldOrFutureHeartbeatsNeverProveThePluginIsRunning(int offset, bool accepting, string expected)
    {
        using var box = new LiteSandbox();
        box.Running = true;
        box.Write("runtime.json", new JsonObject
        {
            ["protocol"] = 1, ["heartbeat_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offset,
            ["backend_running"] = true, ["accepting_requests"] = accepting,
        });
        Assert.Equal(expected, (await box.Client.InspectAsync()).State);
    }

    [Fact]
    public async Task QueriesAndRetriesKeepTheOriginalPendingImportId()
    {
        using var box = new LiteSandbox();
        var original = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited");
        var query = await box.Client.QueryAsync("570");
        var resumed = await new LiteClient(box.Steam, box.Data, () => false).ResumeAsync("570", TimeSpan.Zero);
        Assert.Equal(original.RequestId, query.RequestId);
        Assert.Equal("import", query.Operation);
        Assert.Equal(original.RequestId, resumed!.RequestId);
        await Assert.ThrowsAsync<PreflightException>(() => box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited"));
        Assert.Single(Directory.GetFiles(Path.Combine(box.Mailbox, "inbox"), "*.json"));
    }

    [Fact]
    public async Task CrashRecoveryReusesTheExactWireBytes()
    {
        using var box = new LiteSandbox();
        var original = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited");
        var path = box.RequestPath(original);
        var bytes = File.ReadAllBytes(path);
        File.Move(path, Path.Combine(box.Root, "unpublished-request"));
        var resumed = await box.Client.ResumeAsync("570", TimeSpan.Zero);
        Assert.Equal(original.RequestId, resumed!.RequestId);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task AcceptedReceiptIsDifferentFromSuccessfulImport()
    {
        using var box = new LiteSandbox();
        var request = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited");
        box.Write($"receipts/{request.RequestId}.json", new JsonObject
        {
            ["protocol"] = 1, ["request_id"] = request.RequestId, ["status"] = "accepted",
        });
        var result = await box.Client.ResumeAsync("570", TimeSpan.Zero);
        Assert.Equal("accepted", result!.State);
        Assert.True(result.Pending);
        Assert.Null(result.App);
    }

    [Fact]
    public async Task NewStatusQueriesPreserveTheMostRecentImportResult()
    {
        using var box = new LiteSandbox();
        var import = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited");
        box.Success(import, exists: true, enabled: false);
        var query = await box.Client.QueryAsync("570");
        Assert.NotEqual(import.RequestId, query.RequestId);
        Assert.Equal("query_app", query.Operation);
        Assert.True(query.LastImport!.Succeeded);
        box.Success(query, exists: true, enabled: true);
        var current = await box.Client.ResumeAsync("570", TimeSpan.Zero);
        Assert.True(current!.App!["effective_enabled"]!.GetValue<bool>());
        Assert.False(current.LastImport!.App!["effective_enabled"]!.GetValue<bool>());
        Assert.Equal(import.RequestId, current.LastImport.RequestId);
    }

    [Theory]
    [InlineData("CONFLICT")]
    [InlineData("LOCAL_EDITS_CONFLICT")]
    [InlineData("STALE_REVISION")]
    [InlineData("INVALID_TRANSLATION")]
    [InlineData("APPLY_FAILED")]
    public async Task FailedImportsAreReportedWithoutAutomaticReplacement(string errorCode)
    {
        using var box = new LiteSandbox();
        var import = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited");
        box.Failure(import, errorCode);
        var result = await box.Client.ResumeAsync("570", TimeSpan.Zero);
        Assert.False(result!.Pending);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Single(Directory.GetFiles(Path.Combine(box.Mailbox, "inbox"), "*.json"));
    }

    [Fact]
    public async Task ReusedIdRejectionTakesPriorityOverAnOldSuccess()
    {
        using var box = new LiteSandbox();
        var import = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited");
        box.Success(import, exists: true);
        box.Failure(import, "REQUEST_ID_REUSED", receipt: true);
        var result = await box.Client.ResumeAsync("570", TimeSpan.Zero);
        Assert.Equal("REQUEST_ID_REUSED", result!.ErrorCode);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task MismatchedReceiptsAndIncompleteSuccessCannotBecomeSuccess()
    {
        using var box = new LiteSandbox();
        var import = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited");
        box.Write($"receipts/{import.RequestId}.json", new JsonObject
        {
            ["protocol"] = 1, ["request_id"] = Guid.NewGuid().ToString(), ["status"] = "accepted",
        });
        await Assert.ThrowsAsync<IntegrityException>(() => box.Client.ResumeAsync("570", TimeSpan.Zero));
        File.Move(Path.Combine(box.Mailbox, $"receipts/{import.RequestId}.json"), Path.Combine(box.Root, "bad-receipt"));
        box.Success(import, exists: true);
        var resultPath = Path.Combine(box.Mailbox, $"results/{import.RequestId}.json");
        var result = JsonNode.Parse(File.ReadAllText(resultPath))!.AsObject();
        result["app"]!.AsObject().Remove("revision");
        box.Write($"results/{import.RequestId}.json", result);
        await Assert.ThrowsAsync<IntegrityException>(() => box.Client.ResumeAsync("570", TimeSpan.Zero));
    }

    [Fact]
    public async Task AReportedSuccessfulImportMustActuallyContainASavedApp()
    {
        using var box = new LiteSandbox();
        var import = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited");
        box.Success(import, exists: false);
        await Assert.ThrowsAsync<IntegrityException>(() => box.Client.ResumeAsync("570", TimeSpan.Zero));
    }

    [Fact]
    public async Task UnsupportedTranslationAndInvalidLanguageNeverPublish()
    {
        using var box = new LiteSandbox();
        box.Protocol["translation_versions"] = new JsonArray(1);
        box.Write("protocol.json", box.Protocol);
        await Assert.ThrowsAsync<PreflightException>(() => box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited"));
        box.Protocol["translation_versions"] = new JsonArray(1, 2);
        box.Write("protocol.json", box.Protocol);
        await Assert.ThrowsAsync<IntegrityException>(() => box.Client.ImportAsync("570", LiteSandbox.Translation(), "english", "edited"));
        Assert.False(Directory.Exists(Path.Combine(box.Mailbox, "inbox")));
    }

    [Fact]
    public async Task ExplicitReplacementCarriesOnlyTheConfirmedRevisionAndPermission()
    {
        using var box = new LiteSandbox();
        var conflict = new JsonObject { ["mode"] = "replace", ["expected_revision"] = "revision-1", ["allow_local_edits"] = true };
        var request = await box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited", conflict);
        Assert.Equal(conflict.ToJsonString(), box.Request(request)["conflict"]!.ToJsonString());
    }

    [Fact]
    public async Task CancellationAfterPublicationPreservesTheRequestForResume()
    {
        using var box = new LiteSandbox();
        box.Running = true;
        box.Write("runtime.json", new JsonObject
        {
            ["protocol"] = 1, ["heartbeat_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["backend_running"] = true, ["accepting_requests"] = true,
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => box.Client.ImportAsync("570", LiteSandbox.Translation(), "schinese", "edited", cancellationToken: cancellation.Token));
        box.Running = false;
        var resumed = await box.Client.ResumeAsync("570", TimeSpan.Zero);
        Assert.NotNull(resumed);
        Assert.True(resumed.Pending);
        Assert.Single(Directory.GetFiles(Path.Combine(box.Mailbox, "inbox"), "*.json"));
    }

    [Fact]
    public async Task CliCanValidateAndQueueStandaloneJsonWithoutABinRecord()
    {
        using var box = new LiteSandbox();
        var source = Path.Combine(box.Root, "translation.json");
        File.WriteAllText(source, LiteSandbox.Translation().ToJsonString());
        using var output = new StringWriter();
        var args = new[] { "lite", "import", "570", "--source", source, "--language", "schinese",
            "--steam-dir", box.Steam, "--data-dir", box.Data, "--jsonl", "--yes" };
        Assert.Equal(0, await CliApplication.RunAsync(args, output, TextWriter.Null));
        var events = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var completed = JsonNode.Parse(events.Last())!["payload"]!;
        Assert.True(completed["pending"]!.GetValue<bool>());
        Assert.False(completed["succeeded"]!.GetValue<bool>());
        Assert.Single(Directory.GetFiles(Path.Combine(box.Mailbox, "inbox"), "*.json"));
    }

    [Fact]
    public async Task CliFirstCatalogJsonInstallationNeedsNoBinOrManagedRecord()
    {
        using var box = new LiteSandbox();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(LiteSandbox.Translation());
        var identity = FileOperations.Sha256(bytes);
        var cache = Path.Combine(box.Data, "cache");
        Directory.CreateDirectory(Path.Combine(cache, "translations"));
        File.WriteAllBytes(Path.Combine(cache, "translations", $"v2-{identity}.json"), bytes);
        var catalog = new JsonObject
        {
            ["version"] = 2,
            ["games"] = new JsonObject
            {
                ["570"] = new JsonObject
                {
                    ["name"] = "Test game",
                    ["variants"] = new JsonObject
                    {
                        ["default"] = new JsonObject
                        {
                            ["format"] = "json", ["achievements"] = 1, ["languages"] = new JsonArray("schinese"),
                            ["json"] = new JsonObject { ["version"] = 2, ["size"] = bytes.Length, ["sha256"] = identity },
                        },
                    },
                },
            },
        };
        File.WriteAllText(Path.Combine(cache, "index.json"), catalog.ToJsonString());
        using var output = new StringWriter();
        var args = new[] { "lite", "import", "570", "--catalog", "--variant", "default", "--language", "schinese",
            "--steam-dir", box.Steam, "--data-dir", box.Data, "--offline", "--jsonl", "--yes" };
        Assert.Equal(0, await CliApplication.RunAsync(args, output, TextWriter.Null));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(box.Mailbox, "inbox"), "*.json"));
        var request = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("catalog_json", request["source"]!["kind"]!.GetValue<string>());
        Assert.Equal($"catalog-json:{identity}", request["source"]!["version"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(box.Data, "state.json")));
        Assert.False(Directory.Exists(Path.Combine(cache, "schemas")));
        Assert.False(Directory.Exists(Path.Combine(box.Steam, "appcache")));
    }
}

internal sealed class LiteSandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "satli-lite-tests", Guid.NewGuid().ToString("N"));
    public string Steam => Path.Combine(Root, "Steam");
    public string Data => Path.Combine(Root, "data");
    public string Mailbox => Path.Combine(Steam, "millennium/config/satli-lite/external/v1");
    public bool Running { get; set; }
    public LiteClient Client { get; }
    public JsonObject Protocol { get; } = new()
    {
        ["protocol"] = 1, ["plugin_id"] = "com.gaboron.satli-lite", ["plugin_version"] = "0.1.2",
        ["operations"] = new JsonArray("import", "query_app"), ["translation_versions"] = new JsonArray(1, 2),
    };

    public LiteSandbox(bool millennium = true, bool plugin = true)
    {
        Directory.CreateDirectory(Steam);
        File.WriteAllText(Path.Combine(Steam, "steam.exe"), "");
        if (millennium) Touch("millennium/lib/millennium.dll");
        if (plugin) Touch("millennium/plugins/satli-lite.star");
        Write("protocol.json", Protocol);
        Client = new LiteClient(Steam, Data, () => Running);
    }

    private void Touch(string relative)
    {
        var path = Path.Combine(Steam, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    public string RequestPath(LiteResult result) => Path.Combine(Mailbox, $"inbox/{result.RequestId}.json");
    public JsonObject Request(LiteResult result) => JsonNode.Parse(File.ReadAllText(RequestPath(result)))!.AsObject();
    public void Write(string relative, JsonObject value)
    {
        var path = Path.Combine(Mailbox, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value.ToJsonString(), new UTF8Encoding(false));
    }

    public void Success(LiteResult request, bool exists, bool enabled = true)
    {
        var app = new JsonObject { ["app_id"] = "570", ["exists"] = exists };
        if (exists)
        {
            app["saved"] = true;
            app["enabled"] = enabled;
            app["globally_enabled"] = true;
            app["effective_enabled"] = enabled;
            app["local_edits"] = true;
            app["variant_id"] = "default";
            app["language"] = "schinese";
            app["revision"] = "revision-1";
        }
        Write($"results/{request.RequestId}.json", new JsonObject
        {
            ["protocol"] = 1, ["request_id"] = request.RequestId, ["operation"] = request.Operation,
            ["client_id"] = LiteClient.ClientId, ["ok"] = true, ["status"] = "succeeded",
            ["processed_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["app"] = app,
        });
    }

    public void Failure(LiteResult request, string code, bool receipt = false) => Write($"{(receipt ? "receipts" : "results")}/{request.RequestId}.json", new JsonObject
    {
        ["protocol"] = 1, ["request_id"] = request.RequestId, ["status"] = "failed", ["ok"] = false,
        ["processed_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["error"] = new JsonObject { ["code"] = code },
    });

    public static JsonObject Translation() => LiteTranslation.FromRows("570", "default",
    [new(1, "ACH_FIRST", new Dictionary<string, Satli.Core.Formats.AchievementTranslation>
    {
        ["schinese"] = new("第一步", "完成一场比赛。"),
    })]);

    public void Dispose()
    {
        var path = Path.GetFullPath(Root);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "satli-lite-tests")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test directory");
        RecycleBin.DirectoryIfExists(path);
    }
}
