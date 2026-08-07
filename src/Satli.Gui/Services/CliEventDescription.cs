using Satli_Gui.Models;

namespace Satli_Gui.Services;

public static class CliEventDescription
{
    public static string Format(SatliEvent satliEvent)
    {
        var appId = satliEvent.Payload.TryGetProperty("app_id", out var appIdValue)
            ? $"，App ID {appIdValue.GetString()}"
            : string.Empty;
        var variant = satliEvent.Payload.TryGetProperty("variant_id", out var variantValue)
            ? $"，版本 {variantValue.GetString()}"
            : string.Empty;
        var message = satliEvent.Payload.TryGetProperty("message", out var messageValue)
            ? $"：{messageValue.GetString()}"
            : string.Empty;
        return $"事件 {satliEvent.Event}{appId}{variant}{message}";
    }
}
