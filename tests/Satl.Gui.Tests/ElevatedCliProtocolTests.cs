using Satl_Gui.Services;
using Xunit;

namespace Satl.Gui.Tests;

public sealed class ElevatedCliProtocolTests
{
    [Fact]
    public async Task Request_RoundTripsArgumentsAndSensitiveEnvironmentWithoutCommandLineEncoding()
    {
        var request = new CliInvocation(
            ["install", "730", "--yes", "--jsonl"],
            new Dictionary<string, string>
            {
                ["SATL_PROXY_PASSWORD"] = "空 格 & symbols",
            });
        using var stream = new MemoryStream();

        await ElevatedCliProtocol.WriteRequestAsync(stream, request);
        stream.Position = 0;
        var restored = await ElevatedCliProtocol.ReadRequestAsync(stream);

        Assert.Equal(request.Arguments, restored.Arguments);
        Assert.Equal("空 格 & symbols", restored.Environment["SATL_PROXY_PASSWORD"]);
    }

    [Fact]
    public async Task Response_RoundTripsCliEventsAndDiagnostics()
    {
        var satlEvent = SatlCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"install\",\"event\":\"completed\",\"payload\":{\"succeeded\":1}}");
        var response = new ElevatedCliResponse(0, [satlEvent], string.Empty, ["完成"]);
        using var stream = new MemoryStream();

        await ElevatedCliProtocol.WriteResponseAsync(stream, response);
        stream.Position = 0;
        var restored = await ElevatedCliProtocol.ReadResponseAsync(stream);

        Assert.Equal(0, restored.ExitCode);
        Assert.Single(restored.Events);
        Assert.Equal(1, restored.Events[0].Payload.GetProperty("succeeded").GetInt32());
        Assert.Equal(["完成"], restored.Diagnostics);
    }

    [Theory]
    [InlineData("satl-elevated-0123456789abcdef", true)]
    [InlineData("other-pipe", false)]
    [InlineData("satl-elevated-invalid_name", false)]
    public void ActivationArgument_OnlyAcceptsExpectedPrivatePipeNames(
        string candidate,
        bool expected)
    {
        var parsed = ElevatedCliWorker.TryGetPipeName(
            ["SATLInstaller.exe", ElevatedCliWorker.ActivationArgument, candidate],
            out var pipeName);

        Assert.Equal(expected, parsed);
        Assert.Equal(expected ? candidate : string.Empty, pipeName);
    }
}
