using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class SatliCliServiceTests
{
    [Fact]
    public void ParseEventReadsVersionedPayload()
    {
        var parsed = SatliCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"completed\",\"payload\":{\"count\":4}}"
        );

        Assert.Equal(1, parsed.ProtocolVersion);
        Assert.Equal("scan", parsed.Operation);
        Assert.Equal(4, parsed.Payload.GetProperty("count").GetInt32());
    }

    [Fact]
    public void ParseEventRejectsUnknownProtocol()
    {
        Assert.Throws<InvalidDataException>(() => SatliCliService.ParseEvent(
            "{\"protocol_version\":2,\"operation\":\"scan\",\"event\":\"completed\",\"payload\":{}}"
        ));
    }

    [Fact]
    public void ParseEventPreservesCjkText()
    {
        var parsed = SatliCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"item-succeeded\",\"payload\":{\"game_name\":\"以撒的结合：重生\",\"note_zh\":\"原版\"}}"
        );

        Assert.Equal("以撒的结合：重生", parsed.Payload.GetProperty("game_name").GetString());
        Assert.Equal("原版", parsed.Payload.GetProperty("note_zh").GetString());
    }
}
