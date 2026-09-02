using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.Formats;

namespace Satli.Core.SteamDisplay;

public sealed class SteamDisplayOverrideStore
{
    private const int FormatVersion = 1;
    private const int MaximumBridgeBytes = 32 * 1024 * 1024;
    private JsonObject? _cachedRoot;

    public SteamDisplayOverrideStore(string bridgePath)
    {
        BridgePath = Path.GetFullPath(bridgePath);
    }

    public string BridgePath { get; }

    public bool IsEnabled(string appId) =>
        Load()["apps"]?.AsObject().ContainsKey(appId) is true;

    public void Enable(
        string appId,
        string gameName,
        string schemaPath,
        IEnumerable<string>? sourceSchemaPaths = null)
    {
        var payload = File.ReadAllBytes(schemaPath);
        var preview = BinaryKeyValues.Preview(payload);
        if (preview.AchievementCount == 0)
        {
            throw new PreflightException($"{appId} 的 schema 中没有可用于显示覆盖的成就");
        }

        var sourceRows = preview.Rows.ToDictionary(
            row => row.ApiName,
            row => row.Translations.Values.ToList(),
            StringComparer.Ordinal);
        foreach (var sourcePath in sourceSchemaPaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                continue;
            }
            foreach (var row in BinaryKeyValues.Preview(File.ReadAllBytes(sourcePath)).Rows)
            {
                if (!sourceRows.TryGetValue(row.ApiName, out var sources))
                {
                    continue;
                }
                sources.AddRange(row.Translations.Values);
            }
        }

        var root = Load();
        var apps = root["apps"]!.AsObject();
        var achievements = new JsonObject();
        foreach (var row in preview.Rows)
        {
            var translations = new JsonObject();
            foreach (var pair in row.Translations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                translations[pair.Key] = new JsonObject
                {
                    ["name"] = pair.Value.Name,
                    ["description"] = pair.Value.Description,
                };
            }
            achievements[row.ApiName] = new JsonObject
            {
                ["translations"] = translations,
                ["sources"] = new JsonArray(sourceRows[row.ApiName]
                    .DistinctBy(
                        value => $"{value.Name}\0{value.Description}",
                        StringComparer.Ordinal)
                    .Select(value => (JsonNode)new JsonObject
                    {
                        ["name"] = value.Name,
                        ["description"] = value.Description,
                    })
                    .ToArray()),
            };
        }
        apps[appId] = new JsonObject
        {
            ["game_name"] = gameName,
            ["source_sha256"] = FileOperations.Sha256(payload),
            ["languages"] = new JsonArray(preview.Languages
                .Select(language => JsonValue.Create(language)).ToArray()),
            ["achievements"] = achievements,
        };
        Save(root);
    }

    public void Disable(string appId)
    {
        var root = Load();
        if (root["apps"]!.AsObject().Remove(appId))
        {
            Save(root);
        }
    }

    public void RefreshIfEnabled(string appId, string gameName, string schemaPath)
    {
        if (IsEnabled(appId))
        {
            Enable(appId, gameName, schemaPath);
        }
    }

    private JsonObject Load()
    {
        if (_cachedRoot is not null)
        {
            return _cachedRoot;
        }
        if (!File.Exists(BridgePath))
        {
            return _cachedRoot = Empty();
        }
        try
        {
            if (new FileInfo(BridgePath).Length > MaximumBridgeBytes)
            {
                throw new TransactionException($"Steam 显示桥接文件超过 32 MiB 安全上限：{BridgePath}");
            }
            var root = JsonNode.Parse(File.ReadAllText(BridgePath))?.AsObject()
                ?? throw new JsonException("根节点为空");
            if (root["version"]?.GetValue<int>() != FormatVersion
                || root["apps"] is not JsonObject)
            {
                throw new TransactionException($"Steam 显示桥接文件格式无效：{BridgePath}");
            }
            return _cachedRoot = root;
        }
        catch (TransactionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            throw new TransactionException(
                $"无法读取 Steam 显示桥接文件：{BridgePath}：{exception.Message}",
                exception);
        }
    }

    private void Save(JsonObject root)
    {
        root["generated_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var payload = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        if (payload.Length > MaximumBridgeBytes)
        {
            throw new TransactionException("Steam 显示桥接数据超过 32 MiB 安全上限");
        }
        FileOperations.WriteDurable(
            BridgePath,
            payload.Concat(new byte[] { (byte)'\n' }).ToArray());
        _cachedRoot = root;
    }

    private static JsonObject Empty() => new()
    {
        ["version"] = FormatVersion,
        ["generated_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        ["apps"] = new JsonObject(),
    };

}
