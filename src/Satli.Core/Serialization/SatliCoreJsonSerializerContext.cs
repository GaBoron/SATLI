using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Satli.Core.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal sealed partial class SatliCoreJsonSerializerContext : JsonSerializerContext;
