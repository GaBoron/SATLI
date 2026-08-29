using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;

namespace Satli.Core.SchemaEditing;

public sealed class EditHistoryStore
{
    public EditHistoryStore(string dataDirectory)
    {
        DataDirectory = System.IO.Path.GetFullPath(dataDirectory);
        Path = System.IO.Path.Combine(DataDirectory, "edit-history.json");
    }

    public string DataDirectory { get; }
    public string Path { get; }

    public JsonObject Load()
    {
        if (!File.Exists(Path))
        {
            return new JsonObject { ["version"] = 1, ["apps"] = new JsonObject() };
        }
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path)) as JsonObject
                ?? throw new TransactionException("编辑历史根节点无效");
            if (root["version"]?.GetValue<int>() != 1 || root["apps"] is not JsonObject)
            {
                throw new TransactionException($"不支持的编辑历史版本：{Path}");
            }
            return root;
        }
        catch (SatliException) { throw; }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new TransactionException($"无法读取编辑历史：{Path}：{exception.Message}", exception);
        }
    }

    public void Save(JsonObject state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions { WriteIndented = true });
        FileOperations.WriteDurable(Path, [.. payload, (byte)'\n']);
    }

    public void Add(string appId, JsonObject transaction)
    {
        var state = Load();
        var apps = (JsonObject)state["apps"]!;
        var app = apps[appId] as JsonObject;
        if (app is null)
        {
            app = new JsonObject { ["transactions"] = new JsonArray() };
            apps[appId] = app;
        }
        var transactions = app["transactions"] as JsonArray
            ?? throw new TransactionException($"{appId} 的编辑事务记录无效");
        transactions.Add(transaction);
        Save(state);
    }

    public IReadOnlyList<JsonObject> Transactions(string appId)
    {
        var apps = (JsonObject)Load()["apps"]!;
        if (apps[appId] is not JsonObject app) return [];
        if (app["transactions"] is not JsonArray transactions)
            throw new TransactionException($"{appId} 的编辑事务记录无效");
        return transactions.Select(item => item as JsonObject
                ?? throw new TransactionException($"{appId} 的编辑事务记录无效"))
            .ToArray();
    }

    public JsonObject? Active(string appId) => Transactions(appId).LastOrDefault(item =>
        string.IsNullOrWhiteSpace(item["restored_at"]?.GetValue<string>()));

    public IReadOnlyList<string> ManagedAppIds() => ((JsonObject)Load()["apps"]!).Select(pair => pair.Key)
        .OrderBy(value => ulong.TryParse(value, out var number) ? number : ulong.MaxValue).ToArray();

    public void MarkRestored(string appId, string transactionId, string restoredAt, string? forcedArchive)
    {
        var state = Load();
        var transactions = ((JsonObject)((JsonObject)state["apps"]!)[appId]!)["transactions"] as JsonArray
            ?? throw new TransactionException($"找不到编辑事务：{appId}/{transactionId}");
        var transaction = transactions.OfType<JsonObject>().FirstOrDefault(item =>
            item["id"]?.GetValue<string>() == transactionId)
            ?? throw new TransactionException($"找不到编辑事务：{appId}/{transactionId}");
        transaction["restored_at"] = restoredAt;
        if (!string.IsNullOrWhiteSpace(forcedArchive)) transaction["forced_archive"] = forcedArchive;
        Save(state);
    }
}
