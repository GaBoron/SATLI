using System.Text.Json.Nodes;

namespace Satli.Core.SatliLite;

public sealed record LiteConnection(string State, string Message, IReadOnlyList<int> TranslationVersions)
{
    public bool Connected => State == "connected";
}

public sealed record LiteResult(string RequestId, string Operation, string State, JsonObject? App, string ErrorCode = "")
{
    public bool Pending => State == "pending";
    public bool Succeeded => State == "succeeded";
}
