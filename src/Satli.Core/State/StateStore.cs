using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.Serialization;

namespace Satli.Core.State;

public sealed class StateStore
{
    private const int Version = 1;

    public StateStore(string dataDirectory)
    {
        DataDirectory = System.IO.Path.GetFullPath(dataDirectory);
        Path = System.IO.Path.Combine(DataDirectory, "state.json");
    }

    public string DataDirectory { get; }
    public string Path { get; }

    public JsonObject Load()
    {
        if (!File.Exists(Path))
        {
            return Empty();
        }
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path))?.AsObject()
                ?? throw new JsonException("根节点为空");
            if (root["version"]?.GetValue<int>() != Version)
            {
                throw new TransactionException($"不支持的状态文件版本：{Path}");
            }
            if (root["apps"] is not JsonObject)
            {
                throw new TransactionException($"状态文件 apps 字段无效：{Path}");
            }
            return root;
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
            throw new TransactionException($"无法读取状态文件：{Path}：{exception.Message}", exception);
        }
    }

    public void Save(JsonObject state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            state,
            SatliCoreJsonSerializerContext.Default.JsonObject);
        FileOperations.WriteDurable(Path, payload.Concat(new byte[] { (byte)'\n' }).ToArray());
    }

    public void AddTransaction(string appId, JsonObject transaction)
    {
        var state = Load();
        var apps = state["apps"]!.AsObject();
        if (apps[appId] is null)
        {
            apps[appId] = new JsonObject { ["transactions"] = new JsonArray() };
        }
        var transactions = apps[appId]?["transactions"] as JsonArray
            ?? throw new TransactionException($"{appId} 的事务记录无效");
        transactions.Add(transaction.DeepClone());
        Save(state);
    }

    public void MarkRestored(
        string appId,
        string transactionId,
        string restoredAt,
        string? forcedArchive)
    {
        var state = Load();
        foreach (var transaction in TransactionsFrom(state, appId))
        {
            if (String(transaction, "id") != transactionId)
            {
                continue;
            }
            transaction["restored_at"] = restoredAt;
            if (!string.IsNullOrWhiteSpace(forcedArchive))
            {
                transaction["forced_archive"] = forcedArchive;
            }
            Save(state);
            return;
        }
        throw new TransactionException($"找不到事务：{appId}/{transactionId}");
    }

    public IReadOnlyList<JsonObject> Transactions(string appId) =>
        TransactionsFrom(Load(), appId).Select(value => (JsonObject)value.DeepClone()).ToArray();

    public JsonObject? ActiveTransaction(string appId) =>
        Transactions(appId).LastOrDefault(transaction => string.IsNullOrWhiteSpace(String(transaction, "restored_at")));

    public IReadOnlyList<string> ManagedAppIds() =>
        Load()["apps"]!.AsObject().Select(pair => pair.Key)
            .OrderBy(value => long.Parse(value))
            .ToArray();

    public static string? String(JsonObject value, string name)
    {
        try
        {
            var text = value[name]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static bool Boolean(JsonObject value, string name) =>
        value[name]?.GetValue<bool>() is true;

    private static JsonObject Empty() => new()
    {
        ["version"] = Version,
        ["apps"] = new JsonObject(),
    };

    private static IReadOnlyList<JsonObject> TransactionsFrom(JsonObject state, string appId)
    {
        var app = state["apps"]?.AsObject()[appId];
        if (app is null)
        {
            return [];
        }
        if (app is not JsonObject appObject
            || appObject["transactions"] is not JsonArray transactions
            || transactions.Any(item => item is not JsonObject))
        {
            throw new TransactionException($"{appId} 的状态记录无效");
        }
        return transactions.Select(item => item!.AsObject()).ToArray();
    }
}
