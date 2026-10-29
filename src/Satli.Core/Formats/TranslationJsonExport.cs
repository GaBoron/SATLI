using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;

namespace Satli.Core.Formats;

/// <summary>Exports independent display text without changing Steam, backups or revision history.</summary>
public static class TranslationJsonExport
{
    public static JsonObject FromBin(byte[] schema, string appId, string variantId)
    {
        var translation = TranslationJson.FromBin(schema, appId, standalone: true);
        // Export must reject an invalid explicit variant rather than substituting a local ID.
        translation["variant_id"] = variantId;
        return TranslationJson.Parse(Serialize(translation), appId);
    }

    public static JsonObject Write(byte[] schema, string appId, string variantId, string output)
    {
        output = Path.GetFullPath(output);
        var expectedName = $"UserGameStatsSchema_{appId}.json";
        if (Path.GetFileName(output) != expectedName)
            throw new UsageException($"翻译 JSON 文件名必须是 {expectedName}");
        var translation = FromBin(schema, appId, variantId);
        var bytes = Serialize(translation);
        TranslationJson.Parse(bytes, appId);
        FileOperations.WriteDurable(output, bytes);
        return new JsonObject
        {
            ["app_id"] = appId,
            ["variant_id"] = variantId,
            ["format"] = "json",
            ["output"] = output,
            ["output_sha256"] = FileOperations.Sha256(bytes),
            ["achievement_count"] = translation["achievements"]!.AsObject().Count,
            ["submission_languages"] = translation["languages"]!.DeepClone(),
            ["validated"] = true,
        };
    }

    /// <summary>Stable key ordering, UTF-8 without BOM, LF indentation and a final newline.</summary>
    public static byte[] Serialize(JsonObject translation)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
            WriteNode(writer, translation);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is JsonObject map)
        {
            writer.WriteStartObject();
            foreach (var pair in map.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                WriteNode(writer, pair.Value);
            }
            writer.WriteEndObject();
        }
        else if (node is JsonArray array)
        {
            writer.WriteStartArray();
            foreach (var item in array) WriteNode(writer, item);
            writer.WriteEndArray();
        }
        else if (node is null) writer.WriteNullValue();
        else node.WriteTo(writer);
    }
}
