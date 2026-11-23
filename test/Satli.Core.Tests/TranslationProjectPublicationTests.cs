using Satli.Core.Formats;
using Satli.Core.TranslationProjects;
using Xunit;

namespace Satli.Core.Tests;

public sealed class TranslationProjectPublicationTests
{
    private const string AchievementId = "achievement_fezzan_corridors";

    [Theory]
    [InlineData("english", "As Fezzan, control at least 90% of the trade power in Tunis, Katsina, Safi and Timbuktu.")]
    [InlineData("french", "Contrôler 90\u00a0% de la puissance commerciale.")]
    [InlineData("german", "Kontrolliere 90% der Handelsmacht.")]
    [InlineData("spanish", "Controla el 90% del poder comercial.")]
    public void PublicationAcceptsReportedPercentageDescriptions(string language, string original)
    {
        const string translation = "作为费赞，在突尼斯、卡齐纳、萨非和廷巴克图贸易节点中均控制至少90%的贸易力量。";
        var project = CreateProject(new() { [language] = new("Fezzan Corridors", original) });
        project.Edits[AchievementId] = new("费赞走廊", translation);

        var published = project.Build(forPublication: true);

        Assert.Equal(translation, Assert.Single(TranslationProject.ReadRows(published)).Translations["schinese"].Description);
    }

    [Theory]
    [InlineData("90%d", "%d")]
    [InlineData("90%1$s", "%1$s")]
    [InlineData("90% 06.2f", "% 06.2f")]
    [InlineData("90% d items", "% d")]
    [InlineData("100% damage for {count} turns", "{count}")]
    [InlineData("90 % d’énergie pour {count} tours", "{count}")]
    [InlineData("Show %%d after {count} turns", "{count}")]
    [InlineData("100%%%d", "%d")]
    public void PublicationStillRequiresGenuinePlaceholders(string original, string token)
    {
        var project = CreateProject(new() { ["english"] = new("Fezzan Corridors", original) });
        project.Edits[AchievementId] = new("费赞走廊", $"完成 {token} 次。");
        project.Build(forPublication: true);

        project.Edits[AchievementId] = new("费赞走廊", "完成一次。");
        var error = Assert.Throws<IntegrityException>(() => project.Build(forPublication: true));

        Assert.Contains($"原文占位符：[{token}]", error.Message);
        Assert.Contains("译文占位符：无", error.Message);
    }

    [Fact]
    public void MismatchShowsSourceAlternativesAndRepeatedTokens()
    {
        var project = CreateProject(new()
        {
            ["english"] = new("Fezzan Corridors", "90% of the trade power for %d rounds."),
            ["french"] = new("Couloirs du Fezzan", "90 % de la puissance pendant {count} tours."),
        });
        project.Edits[AchievementId] = new("费赞走廊", "完成 {count} 次。");
        project.Build(forPublication: true);

        project.Edits[AchievementId] = new("费赞走廊", "完成 %1$s 和 %1$s。");

        var error = Assert.Throws<IntegrityException>(() => project.Build(forPublication: true));

        Assert.Contains($"{AchievementId} · description", error.Message);
        Assert.Contains("原文占位符：[%d] 或 [{count}]", error.Message);
        Assert.Contains("译文占位符：[%1$s, %1$s]", error.Message);
    }

    private static TranslationProject CreateProject(Dictionary<string, AchievementTranslation> originals)
    {
        var source = TranslationJson.FromRows("236850", "default", [new(1, AchievementId, originals)]);
        return TranslationProject.Create(source, "Europa Universalis IV", "schinese", "test fixture");
    }
}
