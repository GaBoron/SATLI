using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Satli.Cli;
using Satli.Core.FileSystem;
using Satli.Core.SatliLite;
using Satli_Gui.Services;
using Xunit;

namespace Satli.Core.Tests;

public sealed class LitePluginInstallerTests
{
    [Fact]
    public async Task InstallsOnlyTheOfficialAssetAndPreservesPrivateData()
    {
        using var box = new LiteSandbox(plugin: false);
        var state = PreservePrivateFile(box);
        var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        var result = await LitePluginInstaller.InstallAsync(box.Steam, client);
        Assert.False(result.AlreadyInstalled);
        Assert.Equal(LitePluginInstaller.PluginPath(box.Steam), result.PluginPath);
        Assert.Equal(handler.Payload, File.ReadAllBytes(result.PluginPath));
        Assert.Equal(2, handler.Requests);
        Assert.Equal("private marker", File.ReadAllText(state));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(result.PluginPath)!, "*.part"));
    }

    [Fact]
    public async Task ExistingPluginIsPreservedWithoutAnyNetworkRequest()
    {
        using var box = new LiteSandbox();
        var path = LitePluginInstaller.PluginPath(box.Steam);
        File.WriteAllText(path, "existing plugin");
        var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        var result = await LitePluginInstaller.InstallAsync(box.Steam, client);
        Assert.True(result.AlreadyInstalled);
        Assert.Equal(0, handler.Requests);
        Assert.Equal("existing plugin", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign_url")]
    [InlineData("wrong_tag")]
    [InlineData("digest")]
    [InlineData("size")]
    [InlineData("not_uploaded")]
    public async Task InvalidReleaseMetadataNeverDownloadsOrPublishesAPlugin(string invalid)
    {
        using var box = new LiteSandbox(plugin: false);
        var handler = new ReleaseHandler();
        var asset = handler.Metadata["assets"]![0]!.AsObject();
        switch (invalid)
        {
            case "draft": handler.Metadata["draft"] = true; break;
            case "prerelease": handler.Metadata["prerelease"] = true; break;
            case "missing": handler.Metadata["assets"] = new JsonArray(); break;
            case "duplicate": handler.Metadata["assets"]!.AsArray().Add(asset.DeepClone()); break;
            case "foreign_url": asset["browser_download_url"] = "https://example.com/satli-lite.star"; break;
            case "wrong_tag": handler.Metadata["tag_name"] = "v2.0.0"; break;
            case "digest": asset["digest"] = ""; break;
            case "size": asset["size"] = 0; break;
            case "not_uploaded": asset["state"] = "new"; break;
        }
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<IntegrityException>(() => LitePluginInstaller.InstallAsync(box.Steam, client));
        Assert.Equal(1, handler.Requests);
        Assert.False(File.Exists(LitePluginInstaller.PluginPath(box.Steam)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrTruncatedDownloadsNeverReachThePluginDirectory(bool truncated)
    {
        using var box = new LiteSandbox(plugin: false);
        var handler = new ReleaseHandler { DownloadBytes = truncated ? [1, 2] : [9, 8, 7, 6] };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<IntegrityException>(() => LitePluginInstaller.InstallAsync(box.Steam, client));
        Assert.False(File.Exists(LitePluginInstaller.PluginPath(box.Steam)));
    }

    [Fact]
    public async Task AnInstallationAppearingDuringDownloadIsNeverOverwritten()
    {
        using var box = new LiteSandbox(plugin: false);
        var path = LitePluginInstaller.PluginPath(box.Steam);
        var handler = new ReleaseHandler
        {
            BeforeDownload = () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "installed elsewhere");
            },
        };
        using var client = new HttpClient(handler);
        var result = await LitePluginInstaller.InstallAsync(box.Steam, client);
        Assert.True(result.AlreadyInstalled);
        Assert.Equal("installed elsewhere", File.ReadAllText(path));
    }

    [Fact]
    public async Task CancellationBeforePublicationLeavesPrivateDataAndNoNewPlugin()
    {
        using var box = new LiteSandbox(plugin: false);
        var state = PreservePrivateFile(box);
        using var cancellation = new CancellationTokenSource();
        var handler = new ReleaseHandler { BeforeDownload = cancellation.Cancel };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LitePluginInstaller.InstallAsync(box.Steam, client, cancellation.Token));
        Assert.False(File.Exists(LitePluginInstaller.PluginPath(box.Steam)));
        Assert.Equal("private marker", File.ReadAllText(state));
    }

    [Fact]
    public async Task NetworkTimeoutIsNotReportedAsUserCancellation()
    {
        using var box = new LiteSandbox(plugin: false);
        using var client = new HttpClient(new ReleaseHandler { SimulateTimeout = true });
        await Assert.ThrowsAsync<PreflightException>(() => LitePluginInstaller.InstallAsync(box.Steam, client));
        Assert.False(File.Exists(LitePluginInstaller.PluginPath(box.Steam)));
    }

    [Fact]
    public async Task HttpFailureDoesNotInstallAnything()
    {
        using var box = new LiteSandbox(plugin: false);
        using var client = new HttpClient(new ReleaseHandler { Status = HttpStatusCode.ServiceUnavailable });
        await Assert.ThrowsAsync<HttpRequestException>(() => LitePluginInstaller.InstallAsync(box.Steam, client));
        Assert.False(File.Exists(LitePluginInstaller.PluginPath(box.Steam)));
    }

    [Fact]
    public async Task MissingMillenniumAndLegacyBridgeBlockFirstInstallation()
    {
        using var box = new LiteSandbox(millennium: false, plugin: false);
        var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<PreflightException>(() => LitePluginInstaller.InstallAsync(box.Steam, client));
        Directory.CreateDirectory(Path.Combine(box.Steam, "millennium/lib"));
        File.WriteAllText(Path.Combine(box.Steam, "millennium/lib/millennium.dll"), "");
        var legacy = LitePluginInstaller.LegacyPluginPath(box.Steam);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "old bridge");
        await Assert.ThrowsAsync<PreflightException>(() => LitePluginInstaller.InstallAsync(box.Steam, client));
        Assert.Equal(0, handler.Requests);
        Assert.Equal("old bridge", File.ReadAllText(legacy));
    }

    [Fact]
    public void RetiringTheLegacyBridgeRequiresStoppedSteamAndPreservesAllOtherFiles()
    {
        using var box = new LiteSandbox();
        var state = PreservePrivateFile(box);
        var legacy = LitePluginInstaller.LegacyPluginPath(box.Steam);
        File.WriteAllText(legacy, "old bridge");
        var snapshot = Path.Combine(box.Steam, "millennium/config/satli-bridge-v1.json");
        File.WriteAllText(snapshot, "old snapshot");
        Assert.Throws<PreflightException>(() => LitePluginInstaller.RetireLegacyBridge(box.Steam, () => true));
        Assert.True(File.Exists(legacy));
        Assert.True(LitePluginInstaller.RetireLegacyBridge(box.Steam, () => false));
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(LitePluginInstaller.PluginPath(box.Steam)));
        Assert.Equal("old snapshot", File.ReadAllText(snapshot));
        Assert.Equal("private marker", File.ReadAllText(state));
        Assert.False(LitePluginInstaller.RetireLegacyBridge(box.Steam, () => false));
    }

    [Fact]
    public async Task CliReportsAnExistingInstallationAsANetworkFreeNoOpInOfflineMode()
    {
        using var box = new LiteSandbox();
        using var output = new StringWriter();
        var result = await CliApplication.RunAsync(["lite-install", "--steam-dir", box.Steam,
            "--data-dir", box.Data, "--offline", "--yes", "--jsonl"], output, TextWriter.Null);
        Assert.Equal(0, result);
        var completed = JsonNode.Parse(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last())!;
        Assert.True(completed["payload"]!["already_installed"]!.GetValue<bool>());
        Assert.Contains("保留现有安装", completed["payload"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public void OnlyPluginInstallationAndLegacyRetirementRequireElevation()
    {
        Assert.True(CliElevationPolicy.RequiresElevation(["lite-install", "--yes"]));
        Assert.True(CliElevationPolicy.RequiresElevation(["lite", "retire-legacy", "--yes"]));
        Assert.False(CliElevationPolicy.RequiresElevation(["lite", "inspect"]));
        Assert.False(CliElevationPolicy.RequiresElevation(["lite", "status", "570"]));
    }

    private static string PreservePrivateFile(LiteSandbox box)
    {
        var path = Path.Combine(box.Steam, "millennium/config/satli-lite/state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "private marker");
        return path;
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public byte[] Payload { get; } = [1, 2, 3, 4];
        public byte[]? DownloadBytes { get; init; }
        public JsonObject Metadata { get; }
        public int Requests { get; private set; }
        public Action? BeforeDownload { get; init; }
        public bool SimulateTimeout { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public ReleaseHandler()
        {
            Metadata = new JsonObject
            {
                ["draft"] = false, ["prerelease"] = false, ["tag_name"] = "v1.0.0",
                ["assets"] = new JsonArray(new JsonObject
                {
                    ["name"] = "satli-lite.star", ["state"] = "uploaded", ["size"] = Payload.Length,
                    ["digest"] = $"sha256:{FileOperations.Sha256(Payload)}",
                    ["browser_download_url"] = "https://github.com/GaBoron/SATLI-lite/releases/download/v1.0.0/satli-lite.star",
                }),
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (SimulateTimeout) throw new TaskCanceledException("simulated network timeout");
            var metadata = request.RequestUri!.AbsoluteUri == "https://api.github.com/repos/GaBoron/SATLI-lite/releases/latest";
            if (!metadata) BeforeDownload?.Invoke();
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = metadata ? new StringContent(Metadata.ToJsonString(), Encoding.UTF8, "application/json")
                    : new ByteArrayContent(DownloadBytes ?? Payload),
            });
        }
    }
}
