using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl.Gui.Tests;

public sealed class TranslationUpdateDiffServiceTests
{
    [Fact]
    public async Task CreateAsyncBuildsDiffsOnlyForCatalogUpdates()
    {
        var update = UpdateGame("123");
        var freshInstall = new GameItem { AppId = "456", GameName = "Fresh Game" };
        var loaded = new List<string>();
        var service = new TranslationUpdateDiffService();

        var results = await service.CreateAsync(
            [update, freshInstall],
            [Preview("456", "Fresh Game", "新安装"), Preview("123", "Update Game", "新名称")],
            game =>
            {
                loaded.Add(game.AppId);
                return Task.FromResult<ReplacementPreview?>(
                    Preview(game.AppId, game.GameName, "旧名称"));
            });

        var result = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<TranslationUpdateDiff>>(results));
        Assert.Same(update, result.Game);
        Assert.Equal(["123"], loaded);
        var row = Assert.Single(result.Diff.RowsFor("schinese"));
        Assert.Equal(RevisionDiffKind.Modified, row.Name.Kind);
        Assert.Equal("旧名称", row.Name.Previous);
        Assert.Equal("新名称", row.Name.Current);
    }

    [Fact]
    public async Task CreateAsyncStopsWhenCurrentPreviewCannotBeRead()
    {
        var result = await new TranslationUpdateDiffService().CreateAsync(
            [UpdateGame("123")],
            [Preview("123", "Update Game", "新名称")],
            _ => Task.FromResult<ReplacementPreview?>(null));

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsyncRejectsMissingTargetPreview()
    {
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new TranslationUpdateDiffService().CreateAsync(
                [UpdateGame("123")],
                [],
                _ => Task.FromResult<ReplacementPreview?>(
                    Preview("123", "Update Game", "旧名称"))));

        Assert.Contains("找不到 App ID 123", exception.Message);
    }

    private static GameItem UpdateGame(string appId)
    {
        var game = new GameItem
        {
            AppId = appId,
            GameName = "Update Game",
            InstalledState = "installed",
            InstalledSource = "catalog",
            InstalledVariantId = "default",
            InstalledSha256 = "old-hash",
        };
        game.Variants.Add(new SchemaVariantOption
        {
            VariantId = "default",
            Primary = true,
            Sha256 = "new-hash",
        });
        game.SelectedVariantId = "default";
        return game;
    }

    private static ReplacementPreview Preview(string appId, string gameName, string name) => new(
        appId,
        gameName,
        "default",
        "replace",
        1,
        ["schinese"],
        [new AchievementPreviewRow(
            0,
            "ACH_ONE",
            new Dictionary<string, AchievementTranslation>(StringComparer.OrdinalIgnoreCase)
            {
                ["schinese"] = new(name, "说明"),
            })]);
}
