namespace Satli_Gui.Models;

public enum ManagedGameFilter
{
    All,
    Locked,
}

public static class ManagedGameFiltering
{
    public static bool Matches(GameItem game, ManagedGameFilter filter) =>
        filter != ManagedGameFilter.Locked || game.FileReadOnly;
}
