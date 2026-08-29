using System.Text.Json.Nodes;

namespace Satli.Core.Protocol;

public sealed record CommandEvent(
    int ProtocolVersion,
    string Operation,
    string Event,
    JsonObject Payload)
{
    public static CommandEvent Create(string operation, string eventName, JsonObject payload) =>
        new(1, operation, eventName, payload);
}

public sealed record CommandResult(
    int ExitCode,
    IReadOnlyList<CommandEvent> Events,
    string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
