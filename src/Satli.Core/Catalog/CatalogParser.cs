using System.Text.Json;
using System.Text.RegularExpressions;
using Satli.Core.Models;

namespace Satli.Core.Catalog;

public static partial class CatalogParser
{
    public const int MaximumCatalogBytes = 8 * 1024 * 1024;
    public const int MaximumSchemaBytes = 32 * 1024 * 1024;

    public static TranslationCatalog Parse(ReadOnlySpan<byte> payload, string source = "")
    {
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogException("翻译目录必须是 JSON 对象");
            }
            var version = RequiredInt(root, "version", "翻译目录");
            return version switch
            {
                1 => ParseV1(root, source),
                2 => ParseV2(root, source),
                _ => throw new CatalogException("仅支持翻译目录 version=1 或 version=2"),
            };
        }
        catch (CatalogException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new CatalogException($"翻译目录不是有效的 UTF-8 JSON：{exception.Message}", exception);
        }
    }

    private static TranslationCatalog ParseV1(JsonElement root, string source)
    {
        if (!root.TryGetProperty("entries", out var rawEntries)
            || rawEntries.ValueKind != JsonValueKind.Array)
        {
            throw new CatalogException("index.json entries 必须是数组");
        }
        var entries = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        var position = 0;
        foreach (var item in rawEntries.EnumerateArray())
        {
            position++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogException($"entries[{position}] 必须是对象");
            }
            var appId = RequiredString(item, "game_id", $"entries[{position}]");
            ValidateAppId(appId);
            if (entries.ContainsKey(appId))
            {
                throw new CatalogException($"重复的 Steam App ID：{appId}");
            }
            var variants = new List<SchemaVariant>();
            if (!item.TryGetProperty("schema_files", out var rawVariants))
            {
                variants.Add(ParseV1Variant(appId, item, false));
            }
            else
            {
                if (rawVariants.ValueKind != JsonValueKind.Array
                    || rawVariants.GetArrayLength() == 0)
                {
                    throw new CatalogException($"{appId} 的 schema_files 必须是非空数组");
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var rawVariant in rawVariants.EnumerateArray())
                {
                    if (rawVariant.ValueKind != JsonValueKind.Object)
                    {
                        throw new CatalogException($"{appId} 的版本记录必须是对象");
                    }
                    var variant = ParseV1Variant(appId, rawVariant, true);
                    if (!seen.Add(variant.VariantId))
                    {
                        throw new CatalogException($"{appId} 包含重复版本：{variant.VariantId}");
                    }
                    variants.Add(variant);
                }
                if (variants.Count(variant => variant.Primary) != 1)
                {
                    throw new CatalogException($"{appId} 必须且只能包含一个 default 主版本");
                }
            }
            entries[appId] = new CatalogEntry(
                appId,
                RequiredString(item, "game_name", appId),
                RequiredString(item, "status", appId),
                OrderVariants(variants),
                ParseContributors(item, appId));
        }
        return new TranslationCatalog(1, entries, source);
    }

    private static TranslationCatalog ParseV2(JsonElement root, string source)
    {
        if (!root.TryGetProperty("games", out var rawGames)
            || rawGames.ValueKind != JsonValueKind.Object)
        {
            throw new CatalogException("version=2 翻译目录的 games 必须是对象");
        }
        var entries = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        foreach (var game in rawGames.EnumerateObject())
        {
            ValidateAppId(game.Name);
            if (!entries.TryAdd(game.Name, null!))
            {
                throw new CatalogException($"重复的 Steam App ID：{game.Name}");
            }
            var item = game.Value;
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogException($"{game.Name} 的游戏记录必须是对象");
            }
            if (!item.TryGetProperty("variants", out var rawVariants)
                || rawVariants.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogException($"{game.Name} 的 variants 必须是包含 default 的非空对象");
            }
            var variants = new List<SchemaVariant>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawVariant in rawVariants.EnumerateObject())
            {
                if (!seen.Add(rawVariant.Name) || rawVariant.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new CatalogException($"{game.Name} 包含无效或重复的版本记录");
                }
                variants.Add(ParseV2Variant(game.Name, rawVariant.Name, rawVariant.Value));
            }
            if (variants.Count == 0 || variants.All(variant => !variant.Primary))
            {
                throw new CatalogException($"{game.Name} 的 variants 必须是包含 default 的非空对象");
            }
            var status = item.TryGetProperty("status", out var rawStatus)
                ? RequiredStringValue(rawStatus, $"{game.Name} 的 status")
                : "current";
            entries[game.Name] = new CatalogEntry(
                game.Name,
                RequiredString(item, "name", game.Name),
                status,
                OrderVariants(variants),
                ParseContributors(item, game.Name));
        }
        return new TranslationCatalog(2, entries, source);
    }

    private static SchemaVariant ParseV1Variant(string appId, JsonElement raw, bool explicitVariant)
    {
        var variantId = explicitVariant
            ? RequiredString(raw, "variant_id", $"{appId} 版本").ToLowerInvariant()
            : "default";
        var primary = explicitVariant
            ? raw.TryGetProperty("primary", out var rawPrimary)
                && rawPrimary.ValueKind == JsonValueKind.True
            : true;
        ValidateVariantId(appId, variantId);
        if (primary != (variantId == "default"))
        {
            throw new CatalogException($"{appId}/{variantId} 的 primary 标记无效");
        }
        var schemaFile = RequiredString(raw, "schema_file", $"{appId}/{variantId}");
        ValidateSchemaPath(appId, variantId, schemaFile, primary);
        var sha256 = RequiredString(raw, "sha256", $"{appId}/{variantId}").ToLowerInvariant();
        ValidateSha256(appId, variantId, sha256);
        var size = RequiredLong(raw, "file_size_bytes", $"{appId}/{variantId}");
        if (size <= 0 || size > MaximumSchemaBytes)
        {
            throw new CatalogException($"{appId}/{variantId} 的 file_size_bytes 无效");
        }
        int? count = null;
        if (raw.TryGetProperty("achievement_count", out var rawCount)
            && rawCount.ValueKind != JsonValueKind.Null)
        {
            if (!rawCount.TryGetInt32(out var parsed) || parsed < 0)
            {
                throw new CatalogException($"{appId}/{variantId} 的 achievement_count 无效");
            }
            count = parsed;
        }
        return new SchemaVariant(
            variantId,
            primary,
            schemaFile,
            sha256,
            size,
            OptionalString(raw, "note_zh"),
            OptionalString(raw, "note_en"),
            count);
    }

    private static SchemaVariant ParseV2Variant(
        string appId,
        string variantId,
        JsonElement raw)
    {
        ValidateVariantId(appId, variantId);
        var sha256 = RequiredString(raw, "sha256", $"{appId}/{variantId}").ToLowerInvariant();
        ValidateSha256(appId, variantId, sha256);
        var noteZh = string.Empty;
        var noteEn = string.Empty;
        if (raw.TryGetProperty("label", out var label))
        {
            if (label.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogException($"{appId}/{variantId} 的 label 必须是对象");
            }
            noteZh = RequiredString(label, "zh", $"{appId}/{variantId}.label");
            noteEn = RequiredString(label, "en", $"{appId}/{variantId}.label");
        }
        return new SchemaVariant(
            variantId,
            variantId == "default",
            VariantPath(appId, variantId),
            sha256,
            NoteZh: noteZh,
            NoteEn: noteEn);
    }

    private static IReadOnlyList<SchemaVariant> OrderVariants(IEnumerable<SchemaVariant> variants) =>
        variants.OrderBy(variant => !variant.Primary)
            .ThenBy(variant => variant.VariantId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ParseContributors(JsonElement item, string context)
    {
        if (!item.TryGetProperty("contributors", out var raw)
            || raw.ValueKind == JsonValueKind.Null)
        {
            return [];
        }
        if (raw.ValueKind != JsonValueKind.Array)
        {
            throw new CatalogException($"{context} 的 contributors 必须是数组");
        }
        var result = new List<string>();
        foreach (var value in raw.EnumerateArray())
        {
            var contributor = RequiredStringValue(value, $"{context} 的 contributors");
            if (!result.Contains(contributor, StringComparer.Ordinal))
            {
                result.Add(contributor);
            }
        }
        return result;
    }

    private static void ValidateSchemaPath(
        string appId,
        string variantId,
        string schemaFile,
        bool primary)
    {
        if (Path.IsPathRooted(schemaFile)
            || schemaFile.Contains('\\')
            || schemaFile.Split('/').Contains("..", StringComparer.Ordinal))
        {
            throw new CatalogException($"{appId}/{variantId} 的 schema_file 越界：{schemaFile}");
        }
        var expected = VariantPath(appId, variantId);
        var legacyDefault = $"files/{appId}/UserGameStatsSchema_{appId}.bin";
        if (schemaFile != expected && (!primary || schemaFile != legacyDefault))
        {
            throw new CatalogException(
                $"{appId}/{variantId} 的 schema_file 必须是 {expected}，实际为 {schemaFile}");
        }
    }

    private static string VariantPath(string appId, string variantId) =>
        $"files/{appId}/{variantId}/UserGameStatsSchema_{appId}.bin";

    private static void ValidateAppId(string appId)
    {
        if (!AppIdRegex().IsMatch(appId))
        {
            throw new CatalogException($"无效的 Steam App ID：{appId}");
        }
    }

    private static void ValidateVariantId(string appId, string variantId)
    {
        if (!VariantIdRegex().IsMatch(variantId))
        {
            throw new CatalogException($"{appId} 的 variant ID 无效：{variantId}");
        }
    }

    private static void ValidateSha256(string appId, string variantId, string sha256)
    {
        if (!Sha256Regex().IsMatch(sha256))
        {
            throw new CatalogException($"{appId}/{variantId} 的 SHA-256 无效");
        }
    }

    private static string RequiredString(JsonElement raw, string name, string context)
    {
        if (!raw.TryGetProperty(name, out var value))
        {
            throw new CatalogException($"{context} 的 {name} 无效");
        }
        return RequiredStringValue(value, $"{context} 的 {name}");
    }

    private static string RequiredStringValue(JsonElement value, string context)
    {
        if (value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new CatalogException($"{context} 无效");
        }
        return value.GetString()!.Trim();
    }

    private static string OptionalString(JsonElement raw, string name) =>
        raw.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int RequiredInt(JsonElement raw, string name, string context)
    {
        if (!raw.TryGetProperty(name, out var value) || !value.TryGetInt32(out var parsed))
        {
            throw new CatalogException($"{context} 的 {name} 无效");
        }
        return parsed;
    }

    private static long RequiredLong(JsonElement raw, string name, string context)
    {
        if (!raw.TryGetProperty(name, out var value) || !value.TryGetInt64(out var parsed))
        {
            throw new CatalogException($"{context} 的 {name} 无效");
        }
        return parsed;
    }

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex VariantIdRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
