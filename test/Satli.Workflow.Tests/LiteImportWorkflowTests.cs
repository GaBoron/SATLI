using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.SatliLite;
using Xunit;

namespace Satli.Workflow.Tests;

public sealed class LiteImportWorkflowTests
{
    [Fact]
    public async Task SlowQueryContinuesToInstallOriginalVariantAndLanguageOnce()
    {
        await using var mailbox = new Mailbox { FirstQueryDelay = TimeSpan.FromSeconds(9) };
        var result = await new LiteImportWorkflow(mailbox.Client).ApplyAsync("570", Translation(), "schinese", "catalog_json",
            () => Task.FromResult(true), _ => throw new Exception("no existing translation"))
            .WaitAsync(TimeSpan.FromSeconds(25));
        Assert.True(result!.Succeeded);
        Assert.Equal("import", result.Operation);
        Assert.Single(mailbox.Requests, request => request["operation"]!.ToString() == "query_app");
        Assert.Single(mailbox.Requests, request => request["operation"]!.ToString() == "import");
        Assert.Equal("reviewed", mailbox.LastImport!["translation"]!["variant_id"]!.ToString());
        Assert.Equal("schinese", mailbox.LastImport["language"]!.ToString());
    }

    [Fact]
    public async Task DisconnectDuringQueryWaitsForRefreshThenInstallsOnce()
    {
        await using var mailbox = new Mailbox();
        var readyCalls = 0;
        mailbox.BeforeFirstQueryReply = () => mailbox.Pulse(false);
        var result = await new LiteImportWorkflow(mailbox.Client).ApplyAsync("570", Translation(), "schinese", "edited",
            () => { readyCalls++; mailbox.Pulse(true); return Task.FromResult(true); }, _ => Task.FromResult(true));
        Assert.True(result!.Succeeded);
        Assert.Equal(2, readyCalls);
        Assert.Single(mailbox.Requests, request => request["operation"]!.ToString() == "import");
    }

    [Fact]
    public async Task CancelAfterConnectionLossDoesNotPublishTranslation()
    {
        await using var mailbox = new Mailbox();
        mailbox.BeforeFirstQueryReply = () => mailbox.Pulse(false);
        var readyCalls = 0;
        var result = await new LiteImportWorkflow(mailbox.Client).ApplyAsync("570", Translation(), "schinese", "edited",
            () => Task.FromResult(++readyCalls == 1), _ => Task.FromResult(true));
        Assert.Null(result);
        Assert.Null(mailbox.LastImport);
    }

    [Fact]
    public async Task LiveOnlyImportDoesNotBecomeAnOfflineQueue()
    {
        await using var mailbox = new Mailbox();
        mailbox.Pulse(false);
        await Assert.ThrowsAsync<LiteConnectionUnavailableException>(() => mailbox.Client.ImportAsync(
            "570", Translation(), "schinese", "edited", requireConnected: true));
        Assert.Empty(Directory.GetFiles(mailbox.Inbox, "*.json"));
        var queued = await mailbox.Client.ImportAsync("570", Translation(), "schinese", "edited");
        Assert.True(queued.Pending); // Explicit CLI-style offline delivery remains available.
    }

    [Fact]
    public async Task ExistingPendingImportIsResumedWithoutDuplicateSubmission()
    {
        await using var mailbox = new Mailbox { FirstImportDelay = TimeSpan.FromSeconds(2) };
        mailbox.Pulse(false);
        var pending = await mailbox.Client.ImportAsync("570", Translation(), "schinese", "edited");
        mailbox.Pulse(true);
        var result = await new LiteImportWorkflow(mailbox.Client).ApplyAsync("570", Translation(), "schinese", "edited",
            () => Task.FromResult(true), _ => Task.FromResult(true));
        Assert.Equal(pending.RequestId, result!.RequestId);
        Assert.Single(Directory.GetFiles(mailbox.Inbox, "*.json"));
    }

    [Fact]
    public async Task ReconnectRequeriesRevisionBeforeAuthorizedReplacement()
    {
        await using var mailbox = new Mailbox { Saved = true };
        var confirmations = 0;
        var result = await new LiteImportWorkflow(mailbox.Client).ApplyAsync("570", Translation(), "schinese", "edited",
            () => { mailbox.Pulse(true); return Task.FromResult(true); }, _ =>
            {
                if (++confirmations == 1) mailbox.Pulse(false);
                return Task.FromResult(true);
            });
        Assert.True(result!.Succeeded);
        Assert.Equal(2, confirmations);
        Assert.Equal("revision-1", mailbox.LastImport!["conflict"]!["expected_revision"]!.ToString());
        Assert.Single(mailbox.Requests, request => request["operation"]!.ToString() == "import");
    }

    private static JsonObject Translation() => JsonNode.Parse("""
        {"version":2,"app_id":"570","variant_id":"reviewed","languages":["schinese"],
         "achievements":{"ACH_ONE":{"translations":{"schinese":{"name":"名称","description":"说明"}}}}}
        """)!.AsObject();

    /// <summary>A file-protocol peer only; no real Steam process, account or installation is accessed.</summary>
    private sealed class Mailbox : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "satli-mailbox-test-" + Guid.NewGuid().ToString("N"));
        private readonly string _protocol;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        private volatile bool _online = true;
        public LiteClient Client { get; }
        public string Inbox => Path.Combine(_protocol, "inbox");
        public List<JsonObject> Requests { get; } = [];
        public JsonObject? LastImport { get; private set; }
        public bool Saved { get; set; }
        public TimeSpan FirstQueryDelay { get; init; }
        public TimeSpan FirstImportDelay { get; init; }
        public Action? BeforeFirstQueryReply { get; set; }

        public Mailbox()
        {
            var steam = Path.Combine(_root, "Steam");
            _protocol = Path.Combine(steam, "millennium", "config", "satli-lite", "external", "v1");
            Directory.CreateDirectory(Inbox);
            Directory.CreateDirectory(Path.Combine(_protocol, "results"));
            var dll = Path.Combine(steam, "millennium", "lib", "millennium.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
            File.WriteAllText(dll, "fixture");
            var plugin = LitePluginInstaller.PluginPath(steam);
            Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
            File.WriteAllText(plugin, "fixture");
            File.WriteAllText(Path.Combine(_protocol, "protocol.json"), """
                {"protocol":1,"plugin_id":"com.gaboron.satli-lite","plugin_version":"0.3.0",
                 "operations":["import","query_app"],"translation_versions":[1,2]}
                """);
            Pulse(true);
            Client = new LiteClient(steam, Path.Combine(_root, "Data"), () => true);
            _worker = RespondAsync();
        }

        public void Pulse(bool online)
        {
            _online = online;
            File.WriteAllText(Path.Combine(_protocol, "runtime.json"), new JsonObject
            {
                ["protocol"] = 1, ["heartbeat_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["backend_running"] = online, ["accepting_requests"] = online,
            }.ToJsonString());
        }

        private async Task RespondAsync()
        {
            var seen = new HashSet<string>();
            var queries = 0;
            var imports = 0;
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    if (_online)
                    {
                        foreach (var path in Directory.GetFiles(Inbox, "*.json"))
                        {
                            if (!seen.Add(path)) continue;
                            var request = JsonNode.Parse(await File.ReadAllTextAsync(path, _stop.Token))!.AsObject();
                            Requests.Add(request);
                            var operation = request["operation"]!.ToString();
                            if (operation == "query_app" && ++queries == 1)
                            {
                                await Task.Delay(FirstQueryDelay, _stop.Token);
                                BeforeFirstQueryReply?.Invoke();
                            }
                            if (operation == "import")
                            {
                                if (++imports == 1) await Task.Delay(FirstImportDelay, _stop.Token);
                                LastImport = request;
                                Saved = true;
                            }
                            var app = new JsonObject { ["app_id"] = "570", ["exists"] = Saved };
                            if (Saved)
                            {
                                foreach (var key in new[] { "saved", "enabled", "globally_enabled", "effective_enabled" }) app[key] = true;
                                app["local_edits"] = true;
                                app["variant_id"] = "reviewed";
                                app["language"] = "schinese";
                                app["revision"] = "revision-1";
                            }
                            var result = new JsonObject
                            {
                                ["protocol"] = 1, ["request_id"] = request["request_id"]!.DeepClone(),
                                ["client_id"] = LiteClient.ClientId, ["operation"] = operation,
                                ["status"] = "succeeded", ["ok"] = true, ["app"] = app,
                                ["processed_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            };
                            var destination = Path.Combine(_protocol, "results", request["request_id"] + ".json");
                            await File.WriteAllTextAsync(destination, result.ToJsonString(), _stop.Token);
                        }
                    }
                    await Task.Delay(20, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _worker;
            _stop.Dispose();
            if (!Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected test cleanup target");
            RecycleBin.DirectoryIfExists(_root);
        }
    }
}
