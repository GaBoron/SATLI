using System.Text.Json;
using System.Text.Json.Nodes;

namespace Satli.Cli;

internal sealed class EventSink(
    bool jsonLines,
    TextWriter output,
    Action<string>? onJsonLine = null)
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
        var line = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        output.WriteLine(line);
        onJsonLine?.Invoke(line);
    }
}
