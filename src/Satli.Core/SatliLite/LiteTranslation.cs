using System.Text.Json.Nodes;
using Satli.Core.Formats;

namespace Satli.Core.SatliLite;

/// <summary>Converts local translation sources for the public lite protocol.</summary>
public static class LiteTranslation
{
    public static JsonObject FromBin(byte[] payload, string appId, string variantId = "local") =>
        TranslationJson.FromBin(payload, appId, variantId);

    public static JsonObject FromRows(string appId, string variantId,
        IReadOnlyList<AchievementPreviewRow> rows, string? sourceSha256 = null) =>
        TranslationJson.FromRows(appId, variantId, rows, sourceSha256);
}
