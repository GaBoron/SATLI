namespace Satli_Gui.Models;

public enum ManagedGameFilter
{
    All,
    Modified,
    Locked,
}

public static class ManagedGameFiltering
{
    public static bool Matches(GameItem game, ManagedGameFilter filter) =>
        filter switch
        {
            ManagedGameFilter.Modified => game.IsModified,
            ManagedGameFilter.Locked => game.DisplayOverrideEnabled,
            _ => true,
        };
}
