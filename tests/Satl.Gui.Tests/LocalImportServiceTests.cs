using System.Text.Json;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl.Gui.Tests;

public sealed class LocalImportServiceTests
{
    [Fact]
    public void ParsePreviewRequiresOneCompleteVerifiedPayload()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "app_id": "123",
              "game_name": "Local Game",
              "variant_id": "local-abcdef123456",
              "action": "replace",
              "source": "C:\\UserGameStatsSchema_123.zip",
              "schema_sha256": "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd",
              "achievement_count": 1,
              "languages": ["schinese"],
              "rows": [
                {
                  "index": 1,
                  "api_name": "ACH_LOCAL",
                  "translations": {
                    "schinese": { "name": "本地", "description": "导入" }
                  }
                }
              ]
            }
            """);
        var result = new CliRunResult(
            0,
            [new SatlEvent(1, "local-import", "item-preview", document.RootElement.Clone())],
            string.Empty);

        var preview = LocalImportService.ParsePreview(result);

        Assert.Equal("123", preview.Replacement.AppId);
        Assert.Equal("Local Game", preview.Replacement.GameName);
        Assert.Equal("ACH_LOCAL", preview.Replacement.Rows[0].ApiName);
        Assert.Equal(64, preview.SchemaSha256.Length);
    }

    [Fact]
    public void ParsePreviewRejectsMissingPreviewEvent()
    {
        var result = new CliRunResult(0, [], string.Empty);

        var exception = Assert.Throws<InvalidDataException>(
            () => LocalImportService.ParsePreview(result));

        Assert.Contains("期望 1 个", exception.Message);
    }
}
