namespace Satli_Gui.Models;

public enum GameInstallFilter
{
    All,
    Uninstalled,
    Installed,
    UpdateAvailable,
    Attention,
}

public sealed record GameInstallFilterOption(GameInstallFilter Value, string Label)
{
    public override string ToString() => Label;
}

public static class GameInstallFiltering
{
    public static IReadOnlyList<GameInstallFilterOption> Options { get; } =
    [
        new(GameInstallFilter.All, "全部"),
        new(GameInstallFilter.Uninstalled, "未安装"),
        new(GameInstallFilter.Installed, "已安装"),
        new(GameInstallFilter.UpdateAvailable, "可更新"),
        new(GameInstallFilter.Attention, "需处理"),
    ];

    private static IReadOnlyList<GameInstallFilterOption> LocalOptions { get; } =
    [
        Options[0],
        Options[1],
        Options[2],
        Options[4],
    ];

    private static IReadOnlyList<GameInstallFilterOption> CloudOptions { get; } =
    [
        Options[1],
        Options[2],
    ];

    public static GameInstallFilterOption UpdateOption => Options[3];

    public static int CountUpdates(IEnumerable<GameItem> games) =>
        games.Count(game => game.IsUpdateAvailable);

    public static IReadOnlyList<GameInstallFilterOption> OptionsFor(GameInventoryScope scope) =>
        scope == GameInventoryScope.Cloud ? CloudOptions : LocalOptions;

    public static bool Matches(
        GameItem game,
        GameInstallFilter filter,
        GameInventoryScope scope)
    {
        if (scope == GameInventoryScope.Cloud)
        {
            return filter == GameInstallFilter.Installed
                ? game.CanViewInstalledTranslation
                : !game.CanViewInstalledTranslation;
        }

        return Matches(game, filter);
    }

    public static bool Matches(GameItem game, GameInstallFilter filter) => filter switch
    {
        GameInstallFilter.Uninstalled => game.InstalledState is "unmanaged" or "restored",
        GameInstallFilter.Installed => game.InstalledState == "installed",
        GameInstallFilter.UpdateAvailable => game.IsUpdateAvailable,
        GameInstallFilter.Attention => game.NeedsAttention,
        _ => true,
    };
}
