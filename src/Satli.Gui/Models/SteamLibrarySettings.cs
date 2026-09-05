using System.Text.Json.Serialization;

namespace Satli_Gui.Models;

public sealed class SteamLibrarySettings
{
    public bool Enabled { get; set; }
    public string SteamId { get; set; } = string.Empty;
    [JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;
    [JsonIgnore]
    public bool ApiKeyChanged { get; set; }
    public string ProtectedApiKey { get; set; } = string.Empty;
}
