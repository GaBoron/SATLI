using System.Text.Json;
using Satli_Gui.Models;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class GameItemTests
{
    [Fact]
    public void MapsVariantsAndState()
    {
        using var document = JsonDocument.Parse(
            "{\"app_id\":\"123\",\"game_name\":\"Game\",\"catalog_status\":\"current\",\"installed_state\":\"modified\",\"installed_variant_id\":\"with-unlock-conditions\",\"discovery\":[\"installed\"],\"variants\":[{\"variant_id\":\"default\",\"primary\":true,\"note_zh\":\"原版\"},{\"variant_id\":\"with-unlock-conditions\",\"primary\":false,\"note_zh\":\"含解锁条件\"}]}"
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.Equal("123", item.AppId);
        Assert.Equal("已安装", item.DiscoveryText);
        Assert.True(item.IsModified);
        Assert.Equal("with-unlock-conditions", item.SelectedVariant?.VariantId);
        Assert.Contains("含解锁条件", item.SelectedVariant?.DisplayName);
        Assert.Contains("with-unlock-conditions · 含解锁条件", item.InstalledVersionText);
        Assert.Equal("with-unlock-conditions", item.SelectedVariantId);

        item.SelectedVariantId = "default";

        Assert.Equal("default", item.SelectedVariant?.VariantId);
        item.SelectedVariantId = string.Empty;
        Assert.Equal("default", item.SelectedVariantId);
    }

    [Fact]
    public void PresentsCatalogContributors()
    {
        using var document = JsonDocument.Parse(
            """{"app_id":"123","game_name":"Game","contributors":["Translator","Reviewer"]}"""
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.Equal(["Translator", "Reviewer"], item.Contributors);
        Assert.Equal("译本作者：Translator、Reviewer", item.ContributorText);
        Assert.Equal("App ID 123 · 译本作者：Translator、Reviewer", item.CloudMetadataText);
    }

    [Fact]
    public void ExplainsMissingCatalogContributors()
    {
        using var document = JsonDocument.Parse(
            """{"app_id":"123","game_name":"Game"}"""
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.Equal("译本作者：未提供", item.ContributorText);
    }

    [Theory]
    [InlineData("current", "索引状态：可用", false)]
    [InlineData("possibly-outdated", "索引状态：可能过期", true)]
    [InlineData("possibly-ineffective", "索引状态：可能不生效", true)]
    [InlineData("broken", "索引状态：已失效", true)]
    [InlineData("pending-review", "索引状态：审核中", true)]
    [InlineData("unknown", "索引状态：未收录", true)]
    [InlineData("future-internal-value", "索引状态：未知状态", true)]
    public void PresentsCatalogStatusClearly(
        string status,
        string expectedText,
        bool expectedWarning)
    {
        using var document = JsonDocument.Parse(
            $$"""{"app_id":"123","game_name":"Game","catalog_status":"{{status}}"}"""
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.Equal(expectedText, item.CatalogText);
        Assert.Equal(expectedWarning, item.HasCatalogWarning);
        if (expectedWarning)
        {
            Assert.NotEmpty(item.CatalogWarningText);
        }
        if (status == "future-internal-value")
        {
            Assert.DoesNotContain(status, item.CatalogText);
            Assert.DoesNotContain(status, item.CatalogWarningText);
        }
    }

    [Fact]
    public void PresentsNativeChineseWithoutMissingTranslationWarning()
    {
        using var document = JsonDocument.Parse(
            """{"app_id":"456","game_name":"Native Chinese Game","catalog_status":"unknown","native_languages":["schinese","english"]}"""
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.True(item.HasNativeChinese);
        Assert.Equal("本游戏自带中文", item.CatalogText);
        Assert.False(item.HasCatalogWarning);
        Assert.Empty(item.CatalogWarningText);
    }

    [Fact]
    public void PresentsLocalImportsAsViewableAndRestorable()
    {
        using var document = JsonDocument.Parse(
            """{"app_id":"123","game_name":"Local Game","catalog_status":"unknown","installed_state":"installed","installed_variant_id":"local-abcdef123456","installed_source":"local-import","installed_at":"2026-07-26T00:00:00Z","installed_sha256":"abcdef"}"""
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.True(item.IsLocalImport);
        Assert.True(item.CanViewInstalledTranslation);
        Assert.True(item.CanRestore);
        Assert.Equal("本地导入译本", item.CatalogText);
        Assert.Equal("来源：本地导入", item.InstalledSourceText);
        Assert.False(item.HasCatalogWarning);
        Assert.Equal("恢复", item.RestoreActionText);
    }

    [Fact]
    public void PresentsLocalEditsAsViewableAndRestorable()
    {
        using var document = JsonDocument.Parse(
            """{"app_id":"123","game_name":"Edited Game","catalog_status":"unknown","installed_state":"installed","installed_variant_id":"local-edit-abcdef123456","installed_source":"local-edit","installed_at":"2026-07-26T00:00:00Z","installed_sha256":"abcdef"}"""
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.True(item.IsLocalEdit);
        Assert.False(item.IsLocalImport);
        Assert.True(item.CanViewInstalledTranslation);
        Assert.True(item.CanRestore);
        Assert.Equal("本地编辑译本", item.CatalogText);
        Assert.Equal("来源：本地编辑", item.InstalledSourceText);
        Assert.False(item.HasCatalogWarning);
        Assert.Equal("恢复", item.RestoreActionText);
    }

    [Fact]
    public void PresentsHighRiskFileProtectionState()
    {
        using var document = JsonDocument.Parse(
            """{"app_id":"123","game_name":"Locked Game","installed_state":"installed","file_read_only":true}""");

        var item = GameItem.FromPayload(document.RootElement);

        Assert.True(item.FileReadOnly);
        Assert.True(item.CanToggleProtection);
        Assert.Equal("强制锁定（高风险）", item.ProtectionStatusText);
        Assert.Equal("解除强制锁定", item.ProtectionActionText);
    }

    [Fact]
    public void InstalledVariantChangeSelectsMatchingCatalogVariant()
    {
        var item = new GameItem { AppId = "123", GameName = "Branch Game" };
        item.Variants.Add(new SchemaVariantOption
        {
            VariantId = "default",
            Primary = true,
        });
        item.Variants.Add(new SchemaVariantOption
        {
            VariantId = "experimental",
        });
        item.SelectedVariantId = "default";

        item.InstalledVariantId = "experimental";

        Assert.Equal("experimental", item.SelectedVariantId);
        Assert.Equal("experimental", item.SelectedVariant?.VariantId);
    }
}
