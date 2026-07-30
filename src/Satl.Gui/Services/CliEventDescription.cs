using Satl_Gui.Models;

namespace Satl_Gui.Services;

public static class CliEventDescription
{
    public static string Format(SatlEvent satlEvent)
    {
        var appId = satlEvent.Payload.TryGetProperty("app_id", out var appIdValue)
            ? $"，App ID {appIdValue.GetString()}"
            : string.Empty;
        var variant = satlEvent.Payload.TryGetProperty("variant_id", out var variantValue)
            ? $"，版本 {variantValue.GetString()}"
            : string.Empty;
        var message = satlEvent.Payload.TryGetProperty("message", out var messageValue)
            ? $"：{messageValue.GetString()}"
            : string.Empty;
        return $"事件 {satlEvent.Event}{appId}{variant}{message}";
    }
}
