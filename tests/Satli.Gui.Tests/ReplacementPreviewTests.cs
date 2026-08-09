using System.Text.Json;
using Satli_Gui.Models;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class ReplacementPreviewTests
{
    [Fact]
    public void ScansLanguagesAndDefaultsToSimplifiedChinese()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "app_id":"105600",
              "game_name":"Terraria",
              "variant_id":"default",
              "action":"replace",
              "achievement_count":1,
              "languages":["schinese","english","japanese"],
              "rows":[{
                "index":1,
                "api_name":"TIMBER",
                "translations":{
                  "schinese":{"name":"木材！！","description":"砍倒第一棵树。"},
                  "english":{"name":"Timber!!","description":"Chop down your first tree."},
                  "token":{"name":"TOKEN_NAME","description":"TOKEN_DESC"}
                }
              }]
            }
            """);

        var preview = ReplacementPreview.FromPayload(document.RootElement, "fallback");

        Assert.Equal("schinese", preview.DefaultLanguage);
        Assert.Equal(["schinese", "english", "japanese"], preview.Languages);
        Assert.Equal("木材！！", preview.Rows[0].TranslationFor("schinese").Name);
        Assert.DoesNotContain("token", preview.Rows[0].Translations.Keys);
    }
}
