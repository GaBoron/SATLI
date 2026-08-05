using Satl_Gui.Models;

namespace Satl_Gui.Services;

public static class TranslationPreviewParser
{
    public static ReplacementPreview ParseCurrent(CliRunResult result, GameItem game)
    {
        var payloads = result.Events
            .Where(item => item.Operation == "schema-inspect" && item.Event == "item-succeeded")
            .Select(item => item.Payload)
            .ToList();
        if (payloads.Count != 1)
        {
            throw new InvalidDataException(
                $"当前翻译预览数量无效：期望 1 个，收到 {payloads.Count} 个。拒绝继续。");
        }
        var preview = ReplacementPreview.FromPayload(payloads[0], game.GameName);
        if (!preview.AppId.Equals(game.AppId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("当前翻译预览的 App ID 与所选游戏不一致。拒绝继续。");
        }
        return preview with
        {
            GameName = game.GameName,
            VariantId = string.IsNullOrWhiteSpace(game.InstalledVariantId)
                ? "当前文件"
                : game.InstalledVariantId,
        };
    }

    public static IReadOnlyList<ReplacementPreview> ParseBatch(
        CliRunResult result,
        IReadOnlyList<GameItem> selected)
    {
        var selectedById = selected.ToDictionary(item => item.AppId);
        var previews = result.Events
            .Where(item => item.Event == "item-preview")
            .Select(item =>
            {
                var appId = item.Payload.TryGetProperty("app_id", out var value)
                    ? value.GetString() ?? string.Empty
                    : string.Empty;
                var fallbackName = selectedById.TryGetValue(appId, out var game)
                    ? game.GameName
                    : appId;
                return ReplacementPreview.FromPayload(item.Payload, fallbackName);
            })
            .ToList();
        if (previews.Count != selected.Count)
        {
            throw new InvalidDataException(
                $"替换预览数量不完整：请求 {selected.Count} 个，收到 {previews.Count} 个。拒绝继续。");
        }
        return previews;
    }
}
