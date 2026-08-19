using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class SteamLanguageCatalogTests
{
    [Fact]
    public void EditorOptionsIncludeNamedPresetsAndExistingCustomCodes()
    {
        var options = SteamLanguageCatalog.CreateEditorOptions(
            ["english", "custom_language", "SCHINESE"]);

        Assert.Equal("schinese", options[0].Code);
        Assert.Equal("简体中文 (schinese)", options[0].DisplayName);
        Assert.Contains(options, option =>
            option.Code == "tchinese" && option.DisplayName == "繁体中文 (tchinese)");
        Assert.Contains(options, option => option.Code == "custom_language");
        Assert.Single(options, option => option.Code == "english");
        Assert.Single(options, option => option.Code == "schinese");
    }

    [Fact]
    public void UnknownLanguageUsesItsCodeAsTheDisplayName()
    {
        var option = SteamLanguageCatalog.CreateOption(" Custom_Language ");

        Assert.Equal("custom_language", option.Code);
        Assert.Equal("custom_language", option.DisplayName);
    }
}
