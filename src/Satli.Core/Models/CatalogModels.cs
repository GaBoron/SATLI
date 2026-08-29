namespace Satli.Core.Models;

public sealed record SchemaVariant(
    string VariantId,
    bool Primary,
    string SchemaFile,
    string Sha256,
    long? FileSizeBytes = null,
    string NoteZh = "",
    string NoteEn = "",
    int? AchievementCount = null);

public sealed record CatalogEntry(
    string AppId,
    string GameName,
    string Status,
    IReadOnlyList<SchemaVariant> Variants,
    IReadOnlyList<string> Contributors)
{
    public SchemaVariant PrimaryVariant() =>
        Variants.First(variant => variant.Primary);

    public SchemaVariant Variant(string variantId) =>
        Variants.First(variant => variant.VariantId.Equals(variantId, StringComparison.Ordinal));
}

public sealed record TranslationCatalog(
    int Version,
    IReadOnlyDictionary<string, CatalogEntry> Entries,
    string Source = "",
    bool FromCache = false);

public sealed record SteamAccount(
    string SteamId,
    string AccountName,
    string PersonaName,
    bool MostRecent = false);

public sealed record OwnedGame(string AppId, string Name = "");

public sealed class DiscoveryRecord(string appId, string gameName = "")
{
    public string AppId { get; } = appId;
    public string GameName { get; set; } = gameName;
    public HashSet<string> Discovery { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Accounts { get; } = new(StringComparer.Ordinal);
}
