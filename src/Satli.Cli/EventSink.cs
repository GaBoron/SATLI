using System.Text.Json;
using System.Text.Json.Nodes;

namespace Satli.Cli;

internal sealed class EventSink(bool jsonLines)
{
    public bool JsonLines { get; } = jsonLines;
    public void Emit(string operation, string eventName, JsonObject payload)
    {
        if (!JsonLines) return;
        var root = new JsonObject
        {
            ["protocol_version"] = 1, ["operation"] = operation,
            ["event"] = eventName, ["payload"] = payload,
        };
        Console.WriteLine(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }
}
