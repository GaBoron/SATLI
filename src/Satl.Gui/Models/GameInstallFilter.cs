namespace Satl_Gui.Models;

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

    public static GameInstallFilterOption UpdateOption => Options[3];

    public static int CountUpdates(IEnumerable<GameItem> games) =>
        games.Count(game => game.IsUpdateAvailable);

    public static bool Matches(GameItem game, GameInstallFilter filter) => filter switch
    {
        GameInstallFilter.Uninstalled => game.InstalledState is "unmanaged" or "restored",
        GameInstallFilter.Installed => game.InstalledState == "installed",
        GameInstallFilter.UpdateAvailable => game.IsUpdateAvailable,
        GameInstallFilter.Attention => game.NeedsAttention,
        _ => true,
    };
}
