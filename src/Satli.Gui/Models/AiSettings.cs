namespace Satli_Gui.Models;

public sealed class AiSettings
{
    public string Protocol { get; set; } = "responses";
    public string Endpoint { get; set; } = "https://api.openai.com/v1/responses";
    public string Model { get; set; } = "";
    public string ProtectedApiKey { get; set; } = "";
    public bool StructuredOutput { get; set; } = true;
    public bool WebSearch { get; set; }
}
