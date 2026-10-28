using System.Text.Json.Nodes;

namespace Satli.Core.SatliLite;

public sealed record LiteConnection(string State, string Message, IReadOnlyList<int> TranslationVersions,
    bool ProtocolCompatible = false, string PluginVersion = "")
{
    public bool Connected => State == "connected";
    public bool CanSubmit => Connected || ProtocolCompatible;
}

public sealed record LiteResult(string RequestId, string Operation, string State, JsonObject? App,
    string ErrorCode = "", long? ProcessedAt = null, LiteResult? LastImport = null)
{
    public bool Pending => State is "prepared" or "submitted" or "accepted" or "pending";
    public bool Succeeded => State == "succeeded";
}
