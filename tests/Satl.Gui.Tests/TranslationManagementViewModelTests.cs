using System.Text.Json;
using Satl_Gui.Models;
using Satl_Gui.ViewModels;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class TranslationManagementViewModelTests
{
    [Fact]
    public void ParseCurrentPreviewUsesManagedGameIdentityAndInstalledVariant()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "app_id": "123",
              "achievement_count": 1,
              "languages": ["schinese"],
              "rows": [
                {
                  "index": 1,
                  "api_name": "ACH_LOCAL",
                  "translations": {
                    "schinese": { "name": "本地", "description": "查看" }
                  }
                }
              ]
            }
            """);
        var result = new CliRunResult(
            0,
            [new SatlEvent(1, "schema-inspect", "item-succeeded", document.RootElement.Clone())],
            string.Empty);
        var game = new GameItem
        {
            AppId = "123",
            GameName = "Local Game",
            InstalledState = "installed",
            InstalledVariantId = "local-abcdef123456",
            InstalledSource = "local-import",
        };

        var preview = TranslationManagementViewModel.ParseCurrentPreview(result, game);

        Assert.Equal("Local Game", preview.GameName);
        Assert.Equal("local-abcdef123456", preview.VariantId);
        Assert.Equal("ACH_LOCAL", preview.Rows[0].ApiName);
        Assert.Equal("本地", preview.Rows[0].TranslationFor("schinese").Name);
    }

    [Fact]
    public void ParseCurrentPreviewRejectsMismatchedAppId()
    {
        using var document = JsonDocument.Parse(
            """{"app_id":"456","achievement_count":1,"languages":[],"rows":[]}""");
        var result = new CliRunResult(
            0,
            [new SatlEvent(1, "schema-inspect", "item-succeeded", document.RootElement.Clone())],
            string.Empty);
        var game = new GameItem { AppId = "123", GameName = "Local Game" };

        Assert.Throws<InvalidDataException>(
            () => TranslationManagementViewModel.ParseCurrentPreview(result, game));
    }
}
