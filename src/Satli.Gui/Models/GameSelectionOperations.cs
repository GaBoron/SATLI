namespace Satli_Gui.Models;

public static class GameSelectionOperations
{
    public static bool AreAllSelected(IEnumerable<GameItem> games)
    {
        var any = false;
        foreach (var game in games)
        {
            any = true;
            if (!game.IsSelected)
            {
                return false;
            }
        }
        return any;
    }

    public static void ToggleVisible(IEnumerable<GameItem> games)
    {
        var items = games as IReadOnlyCollection<GameItem> ?? games.ToArray();
        var select = !AreAllSelected(items);
        foreach (var game in items)
        {
            game.IsSelected = select;
        }
    }

    public static void ClearAll(IEnumerable<GameItem> games)
    {
        foreach (var game in games)
        {
            game.IsSelected = false;
        }
    }
}
