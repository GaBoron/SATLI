using System.Text;

namespace Satli.Core.Formats;

public sealed class BinaryKeyValuesNode(int typeId, string name)
{
    public int TypeId { get; } = typeId;
    public string Name { get; } = name;
    public List<BinaryKeyValuesNode> Children { get; } = [];
    public string? Value { get; set; }
    public byte[] RawValue { get; set; } = [];
}

public sealed record AchievementTranslation(string Name, string Description);

public sealed record AchievementPreviewRow(
    int Index,
    string ApiName,
    IReadOnlyDictionary<string, AchievementTranslation> Translations);

public sealed record AchievementPreview(
    int AchievementCount,
    bool RoundtripEqual,
    IReadOnlyList<string> Languages,
    IReadOnlyList<AchievementPreviewRow> Rows);

public static class BinaryKeyValues
{
    public static System.Text.Json.Nodes.JsonObject PreviewJson(ReadOnlySpan<byte> data)
    {
        var preview = Preview(data);
        return new System.Text.Json.Nodes.JsonObject
        {
            ["achievement_count"] = preview.AchievementCount,
            ["roundtrip_equal"] = preview.RoundtripEqual,
            ["languages"] = new System.Text.Json.Nodes.JsonArray(
                preview.Languages.Select(value => System.Text.Json.Nodes.JsonValue.Create(value)).ToArray()),
            ["rows"] = new System.Text.Json.Nodes.JsonArray(preview.Rows.Select(row =>
            {
                var translations = new System.Text.Json.Nodes.JsonObject();
                foreach (var pair in row.Translations)
                {
                    translations[pair.Key] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["name"] = pair.Value.Name,
                        ["description"] = pair.Value.Description,
                    };
                }
                return new System.Text.Json.Nodes.JsonObject
                {
                    ["index"] = row.Index,
                    ["api_name"] = row.ApiName,
                    ["translations"] = translations,
                };
            }).ToArray()),
        };
    }

    public static List<BinaryKeyValuesNode> Parse(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data.ToArray());
        try
        {
            var nodes = ParseNodes(reader);
            if (reader.Position != data.Length)
            {
                throw new PreflightException(
                    $"Binary KeyValues 解析在偏移 {reader.Position} 停止，文件大小为 {data.Length}");
            }
            return nodes;
        }
        catch (PreflightException)
        {
            throw;
        }
        catch (Exception exception) when (exception is DecoderFallbackException
            or InvalidDataException
            or ArgumentException)
        {
            throw new PreflightException($"无法解析 Binary KeyValues schema：{exception.Message}", exception);
        }
    }

    public static byte[] Serialize(IReadOnlyList<BinaryKeyValuesNode> nodes)
    {
        using var output = new MemoryStream();
        WriteNodes(output, nodes);
        return output.ToArray();
    }

    public static AchievementPreview Preview(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        var nodes = Parse(bytes);
        if (!Serialize(nodes).AsSpan().SequenceEqual(bytes))
        {
            throw new PreflightException("Binary KeyValues schema 未通过字节级 roundtrip 校验");
        }

        var rows = new List<AchievementPreviewRow>();
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bits in Walk(nodes).Where(node => node.TypeId == 0 && node.Name == "bits"))
        {
            foreach (var achievement in bits.Children.Where(child => child.TypeId == 0))
            {
                var displayName = NestedObject(achievement, "display", "name");
                var displayDescription = NestedObject(achievement, "display", "desc");
                var apiName = FirstString(achievement, "name");
                if (displayName is null || displayDescription is null || string.IsNullOrEmpty(apiName))
                {
                    continue;
                }

                var names = LanguageStrings(displayName);
                var descriptions = LanguageStrings(displayDescription);
                var translations = new Dictionary<string, AchievementTranslation>(StringComparer.Ordinal);
                foreach (var language in names.Keys.Concat(descriptions.Keys)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal))
                {
                    var code = language.ToLowerInvariant();
                    if (code is "token" or "tokens")
                    {
                        continue;
                    }
                    languages.Add(code);
                    translations[code] = new AchievementTranslation(
                        names.GetValueOrDefault(language, string.Empty),
                        descriptions.GetValueOrDefault(language, string.Empty));
                }
                rows.Add(new AchievementPreviewRow(rows.Count + 1, apiName, translations));
            }
        }

        var orderedLanguages = languages
            .OrderBy(language => language switch
            {
                "schinese" => 0,
                "tchinese" => 1,
                "english" => 2,
                _ => 3,
            })
            .ThenBy(language => language, StringComparer.Ordinal)
            .ToArray();
        return new AchievementPreview(rows.Count, true, orderedLanguages, rows);
    }

    public static IEnumerable<BinaryKeyValuesNode> Walk(IEnumerable<BinaryKeyValuesNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Walk(node.Children))
            {
                yield return child;
            }
        }
    }

    public static BinaryKeyValuesNode? NestedObject(BinaryKeyValuesNode node, params string[] names)
    {
        BinaryKeyValuesNode? current = node;
        foreach (var name in names)
        {
            current = current?.Children.FirstOrDefault(child => child.TypeId == 0 && child.Name == name);
            if (current is null)
            {
                return null;
            }
        }
        return current;
    }

    public static string FirstString(BinaryKeyValuesNode node, string name) =>
        node.Children.FirstOrDefault(child => child.TypeId == 1 && child.Name == name)?.Value
            ?? string.Empty;

    public static Dictionary<string, string> LanguageStrings(BinaryKeyValuesNode node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in node.Children.Where(child => child.TypeId == 1))
        {
            result.TryAdd(child.Name, child.Value ?? string.Empty);
        }
        return result;
    }

    private static List<BinaryKeyValuesNode> ParseNodes(Reader reader)
    {
        var nodes = new List<BinaryKeyValuesNode>();
        while (true)
        {
            var typeId = reader.ReadByte();
            if (typeId == 8)
            {
                return nodes;
            }
            if (typeId is < 0 or > 7)
            {
                throw new InvalidDataException($"未知 Binary KeyValues 类型 {typeId}");
            }
            var node = new BinaryKeyValuesNode(typeId, reader.ReadCString());
            switch (typeId)
            {
                case 0:
                    node.Children.AddRange(ParseNodes(reader));
                    break;
                case 1:
                    node.RawValue = reader.ReadCStringBytes();
                    node.Value = StrictUtf8.GetString(node.RawValue);
                    break;
                case 2 or 3 or 4 or 6:
                    node.RawValue = reader.ReadBytes(4);
                    break;
                case 7:
                    node.RawValue = reader.ReadBytes(8);
                    break;
                case 5:
                    throw new InvalidDataException("暂不支持 WideString Binary KeyValues 节点");
            }
            nodes.Add(node);
        }
    }

    private static void WriteNodes(Stream output, IReadOnlyList<BinaryKeyValuesNode> nodes)
    {
        foreach (var node in nodes)
        {
            output.WriteByte((byte)node.TypeId);
            output.Write(StrictUtf8.GetBytes(node.Name));
            output.WriteByte(0);
            switch (node.TypeId)
            {
                case 0:
                    WriteNodes(output, node.Children);
                    break;
                case 1:
                    output.Write(node.RawValue);
                    output.WriteByte(0);
                    break;
                case 2 or 3 or 4 or 6 or 7:
                    output.Write(node.RawValue);
                    break;
                default:
                    throw new PreflightException($"无法序列化 Binary KeyValues 类型 {node.TypeId}");
            }
        }
        output.WriteByte(8);
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private sealed class Reader(byte[] data)
    {
        public int Position { get; private set; }

        public int ReadByte()
        {
            if (Position >= data.Length)
            {
                throw new InvalidDataException("读取类型时意外到达文件结尾");
            }
            return data[Position++];
        }

        public byte[] ReadBytes(int count)
        {
            if (Position + count > data.Length)
            {
                throw new InvalidDataException("读取值时意外到达文件结尾");
            }
            var value = data.AsSpan(Position, count).ToArray();
            Position += count;
            return value;
        }

        public byte[] ReadCStringBytes()
        {
            var end = Array.IndexOf(data, (byte)0, Position);
            if (end < 0)
            {
                throw new InvalidDataException("字符串缺少 NUL 结束符");
            }
            var value = data.AsSpan(Position, end - Position).ToArray();
            Position = end + 1;
            return value;
        }

        public string ReadCString() => StrictUtf8.GetString(ReadCStringBytes());
    }
}
