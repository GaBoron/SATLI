using System.Text.Json;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Satl_Gui.ViewModels;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class TranslationManagementViewModelTests
{
    [Fact]
    public void InstallSummaryReportsEveryFailureAndContinuedBatch()
    {
        using var firstFailure = JsonDocument.Parse(
            """{"app_id":"456","game_name":"Game B","message":"拒绝访问"}""");
        using var success = JsonDocument.Parse("""{"app_id":"123","game_name":"Game A"}""");
        using var secondFailure = JsonDocument.Parse(
            """{"app_id":"789","game_name":"Game C","message":"文件被占用"}""");
        using var completed = JsonDocument.Parse("""{"succeeded":1,"failed":2,"exit_code":7}""");
        var result = new CliRunResult(
            7,
            [
                new SatlEvent(1, "install", "item-failed", firstFailure.RootElement.Clone()),
                new SatlEvent(1, "install", "item-succeeded", success.RootElement.Clone()),
                new SatlEvent(1, "install", "item-failed", secondFailure.RootElement.Clone()),
                new SatlEvent(1, "install", "completed", completed.RootElement.Clone()),
            ],
            string.Empty);

        var summary = InstallOperationSummary.TryCreate(result);

        Assert.NotNull(summary);
        Assert.True(summary.HasSucceededItems);
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(2, summary.Failed);
        Assert.Contains("单项失败未中止后续任务", summary.Message);
        Assert.Contains("456 Game B：拒绝访问", summary.Message);
        Assert.Contains("789 Game C：文件被占用", summary.Message);
    }

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

    [Fact]
    public void BatchPreviewParserRejectsIncompleteResponse()
    {
        using var document = JsonDocument.Parse(
            """{"app_id":"123","achievement_count":0,"languages":[],"rows":[]}""");
        var result = new CliRunResult(
            0,
            [new SatlEvent(1, "install", "item-preview", document.RootElement.Clone())],
            string.Empty);
        var selected = new[]
        {
            new GameItem { AppId = "123", GameName = "First" },
            new GameItem { AppId = "456", GameName = "Second" },
        };

        var exception = Assert.Throws<InvalidDataException>(
            () => TranslationPreviewParser.ParseBatch(result, selected));

        Assert.Contains("请求 2 个，收到 1 个", exception.Message);
    }
}
