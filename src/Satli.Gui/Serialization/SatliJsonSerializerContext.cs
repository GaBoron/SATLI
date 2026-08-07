using System.Text.Json.Serialization;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GuiSettings))]
[JsonSerializable(typeof(SatliEvent))]
[JsonSerializable(typeof(CliInvocation))]
[JsonSerializable(typeof(ElevatedCliResponse))]
[JsonSerializable(typeof(WindowPlacement))]
[JsonSerializable(typeof(string))]
internal sealed partial class SatliJsonSerializerContext : JsonSerializerContext;
