using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.Formats;

namespace Satli.Core.SchemaEditing;

public sealed record SchemaRevision(
    string Commit, string AppId, string GameName, string TargetLanguage, string Action,
    string CreatedAt, string SchemaSha256, string ParentSchemaSha256, int AchievementCount,
    int ChangedNames, int ChangedDescriptions, string VariantId, byte[] Schema)
{
    public JsonObject ToJson(bool includePreview = false) => new()
    {
        ["commit"] = Commit, ["short_commit"] = Commit[..Math.Min(12, Commit.Length)],
        ["app_id"] = AppId, ["game_name"] = GameName, ["target_language"] = TargetLanguage,
        ["action"] = Action, ["created_at"] = CreatedAt, ["schema_sha256"] = SchemaSha256,
        ["parent_schema_sha256"] = ParentSchemaSha256, ["achievement_count"] = AchievementCount,
        ["changed_names"] = ChangedNames, ["changed_descriptions"] = ChangedDescriptions,
        ["variant_id"] = VariantId, ["available"] = true,
        ["preview"] = includePreview ? BinaryKeyValues.PreviewJson(Schema) : null,
    };
}

public sealed class SchemaRevisionStore
{
    public SchemaRevisionStore(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        Root = Path.Combine(DataDirectory, "schema-revisions");
    }
    public string DataDirectory { get; }
    public string Root { get; }

    public SchemaRevision Record(string appId, byte[] schema, string action, string gameName = "",
        string targetLanguage = "", int achievementCount = 0, int changedNames = 0,
        int changedDescriptions = 0, string variantId = "")
    {
        if (!appId.All(char.IsAsciiDigit)) throw new UsageException($"无效的 Steam App ID：{appId}");
        var preview = BinaryKeyValues.Preview(schema);
        var sha = Convert.ToHexString(SHA256.HashData(schema)).ToLowerInvariant();
        var previous = List(appId).FirstOrDefault();
        if (previous?.SchemaSha256 == sha) return previous;
        var createdAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var metadata = new JsonObject
        {
            ["version"] = 1, ["app_id"] = appId, ["game_name"] = gameName,
            ["target_language"] = targetLanguage, ["action"] = action, ["created_at"] = createdAt,
            ["schema_sha256"] = sha, ["parent_schema_sha256"] = previous?.SchemaSha256 ?? "",
            ["achievement_count"] = achievementCount > 0 ? achievementCount : preview.AchievementCount,
            ["changed_names"] = changedNames, ["changed_descriptions"] = changedDescriptions,
            ["variant_id"] = variantId,
        };
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, new JsonSerializerOptions { WriteIndented = true });
        var commitInput = metadataBytes.Concat(schema).ToArray();
        var commit = Convert.ToHexString(SHA256.HashData(commitInput)).ToLowerInvariant();
        var directory = Path.Combine(Root, appId, commit);
        if (!Directory.Exists(directory))
        {
            FileOperations.WriteDurable(Path.Combine(directory, "schema.bin"), schema);
            FileOperations.WriteDurable(Path.Combine(directory, "metadata.json"), [.. metadataBytes, (byte)'\n']);
        }
        var indexPath = Path.Combine(Root, appId, "index.json");
        var index = File.Exists(indexPath) ? JsonNode.Parse(File.ReadAllText(indexPath)) as JsonArray : new JsonArray();
        index ??= new JsonArray();
        if (!index.Any(item => item?.GetValue<string>() == commit)) index.Insert(0, commit);
        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(index, new JsonSerializerOptions { WriteIndented = true });
        FileOperations.WriteDurable(indexPath, [.. indexBytes, (byte)'\n']);
        return Read(appId, commit);
    }

    public IReadOnlyList<SchemaRevision> List(string appId)
    {
        var indexPath = Path.Combine(Root, appId, "index.json");
        if (!File.Exists(indexPath))
        {
            ImportLegacyLooseObjects(appId);
        }
        if (!File.Exists(indexPath)) return [];
        try
        {
            var index = JsonNode.Parse(File.ReadAllText(indexPath)) as JsonArray
                ?? throw new TransactionException($"修订索引无效：{indexPath}");
            return index.Select(item => Read(appId, item?.GetValue<string>() ?? "")).ToArray();
        }
        catch (SatliException) { throw; }
        catch (Exception exception) { throw new TransactionException($"无法读取修订索引：{exception.Message}", exception); }
    }

    public SchemaRevision Get(string appId, string revision)
    {
        var matches = List(appId).Where(item => item.Commit.StartsWith(revision, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        {
            1 => matches[0], 0 => throw new UsageException($"找不到修订：{revision}"),
            _ => throw new UsageException($"修订前缀不唯一：{revision}"),
        };
    }

    public JsonObject Verify(string? appId = null)
    {
        var ids = appId is not null ? [appId] : Directory.Exists(Root)
            ? Directory.EnumerateDirectories(Root).Select(Path.GetFileName).Where(value => value is not null).Cast<string>().ToArray()
            : [];
        var revisions = ids.SelectMany(List).ToArray();
        foreach (var revision in revisions)
        {
            var actual = Convert.ToHexString(SHA256.HashData(revision.Schema)).ToLowerInvariant();
            if (actual != revision.SchemaSha256) throw new IntegrityException($"修订 {revision.Commit[..12]} 的 schema SHA-256 不匹配");
            BinaryKeyValues.Preview(revision.Schema);
        }
        return new JsonObject { ["verified"] = revisions.Length, ["repository"] = Root };
    }

    public SchemaRevision Export(string appId, string revision, string output, string format)
    {
        var item = Get(appId, revision);
        if (format == "bin") FileOperations.WriteDurable(output, item.Schema);
        else if (format == "zip") SchemaEditor.WriteZip(output, $"UserGameStatsSchema_{appId}.bin", item.Schema);
        else throw new UsageException($"不支持的导出格式：{format}");
        return item;
    }

    private SchemaRevision Read(string appId, string commit)
    {
        if (commit.Length != 64 || commit.Any(character => !Uri.IsHexDigit(character))) throw new TransactionException($"修订 ID 无效：{commit}");
        var directory = Path.Combine(Root, appId, commit);
        var schema = File.ReadAllBytes(Path.Combine(directory, "schema.bin"));
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "metadata.json")));
        var root = document.RootElement;
        return new SchemaRevision(commit, appId, Text(root, "game_name"), Text(root, "target_language"),
            Text(root, "action"), Text(root, "created_at"), Text(root, "schema_sha256"),
            Text(root, "parent_schema_sha256"), root.GetProperty("achievement_count").GetInt32(),
            root.GetProperty("changed_names").GetInt32(), root.GetProperty("changed_descriptions").GetInt32(),
            Text(root, "variant_id"), schema);
    }

    private void ImportLegacyLooseObjects(string appId)
    {
        var legacy = Path.Combine(DataDirectory, "schema-revisions.git");
        var headPath = Path.Combine(legacy, "refs", "heads", "main");
        if (!File.Exists(headPath)) return;
        try
        {
            var commits = new List<LegacyRevision>();
            var commitId = File.ReadAllText(headPath).Trim();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (commitId.Length == 40 && visited.Add(commitId))
            {
                var commit = LegacyGitObject.Read(legacy, commitId, "commit");
                var text = Encoding.UTF8.GetString(commit);
                var treeId = Header(text, "tree");
                var parent = Header(text, "parent");
                var rootTree = LegacyGitObject.Tree(legacy, treeId);
                if (rootTree.TryGetValue(appId, out var appTreeId))
                {
                    var appTree = LegacyGitObject.Tree(legacy, appTreeId);
                    if (appTree.TryGetValue("schema.bin", out var schemaId)
                        && appTree.TryGetValue("metadata.json", out var metadataId))
                    {
                        var schema = LegacyGitObject.Read(legacy, schemaId, "blob");
                        var metadata = LegacyGitObject.Read(legacy, metadataId, "blob");
                        commits.Add(new LegacyRevision(schema, metadata));
                    }
                }
                commitId = parent;
            }
            commits.Reverse();
            foreach (var legacyRevision in commits)
            {
                using var document = JsonDocument.Parse(legacyRevision.Metadata);
                var metadata = document.RootElement;
                Record(
                    appId,
                    legacyRevision.Schema,
                    Text(metadata, "action"),
                    Text(metadata, "game_name"),
                    Text(metadata, "target_language"),
                    metadata.TryGetProperty("achievement_count", out var count)
                        ? count.GetInt32()
                        : 0,
                    metadata.TryGetProperty("changed_names", out var names)
                        ? names.GetInt32()
                        : 0,
                    metadata.TryGetProperty("changed_descriptions", out var descriptions)
                        ? descriptions.GetInt32()
                        : 0,
                    Text(metadata, "variant_id"));
            }
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or JsonException
            or UnauthorizedAccessException)
        {
            throw new TransactionException(
                $"无法导入旧版 schema 修订记录：{exception.Message}",
                exception);
        }
    }

    private static string Header(string commit, string name)
    {
        var prefix = name + " ";
        var line = commit.Split('\n')
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? "" : line[prefix.Length..].Trim();
    }

    private sealed record LegacyRevision(byte[] Schema, byte[] Metadata);
    private static string Text(JsonElement root, string property) => root.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";
}

internal static class LegacyGitObject
{
    public static byte[] Read(string repository, string objectId, string expectedType)
    {
        if (objectId.Length != 40 || objectId.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"旧版 Git 对象 ID 无效：{objectId}");
        var path = Path.Combine(
            repository,
            "objects",
            objectId[..2],
            objectId[2..]);
        if (!File.Exists(path))
            throw new InvalidDataException($"旧版 Git 松散对象不存在：{objectId}");
        using var source = File.OpenRead(path);
        using var zlib = new System.IO.Compression.ZLibStream(
            source,
            System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        var inflated = output.ToArray();
        var zero = Array.IndexOf(inflated, (byte)0);
        if (zero <= 0)
            throw new InvalidDataException($"旧版 Git 对象头无效：{objectId}");
        var header = Encoding.ASCII.GetString(inflated, 0, zero);
        var parts = header.Split(' ', 2);
        if (parts.Length != 2
            || parts[0] != expectedType
            || !int.TryParse(parts[1], out var size)
            || size != inflated.Length - zero - 1)
            throw new InvalidDataException($"旧版 Git 对象类型或大小无效：{objectId}");
        return inflated[(zero + 1)..];
    }

    public static IReadOnlyDictionary<string, string> Tree(
        string repository,
        string objectId)
    {
        var payload = Read(repository, objectId, "tree");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var offset = 0;
        while (offset < payload.Length)
        {
            var space = Array.IndexOf(payload, (byte)' ', offset);
            var zero = Array.IndexOf(payload, (byte)0, space + 1);
            if (space < offset || zero < space || zero + 21 > payload.Length)
                throw new InvalidDataException($"旧版 Git tree 无效：{objectId}");
            var name = Encoding.UTF8.GetString(payload, space + 1, zero - space - 1);
            var hash = Convert.ToHexString(payload.AsSpan(zero + 1, 20)).ToLowerInvariant();
            result[name] = hash;
            offset = zero + 21;
        }
        return result;
    }
}
