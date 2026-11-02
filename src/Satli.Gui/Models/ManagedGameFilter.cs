namespace Satli_Gui.Models;

public enum ManagedGameFilter
{
    Bin,
    Json,
}

public static class ManagedGameFiltering
{
    public static bool Matches(GameItem game, ManagedGameFilter filter) =>
        filter switch
        {
            ManagedGameFilter.Json => game.LiteInstalled || game.LitePending,
            _ => game.InstalledState != "unmanaged",
        };
}
