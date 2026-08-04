namespace Satl_Gui.Models;

public sealed record TranslationUpdateDiff(
    GameItem Game,
    SchemaRevisionDiff Diff);
