using System.Text;

namespace Satli.Core.Formats;

public static class VdfParser
{
    public static Dictionary<string, object> Parse(string text)
    {
        var tokens = Tokenize(text);
        var position = 0;
        var result = ParseObject(tokens, ref position, false);
        if (position != tokens.Count)
        {
            throw new InvalidDataException("VDF 尾部包含无法解析的数据");
        }
        return result;
    }

    public static Dictionary<string, object> Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path, new UTF8Encoding(true, true)));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DecoderFallbackException
            or InvalidDataException)
        {
            throw new PreflightException($"无法解析 VDF：{path}：{exception.Message}", exception);
        }
    }

    public static object? Get(object? mapping, string key, object? fallback = null)
    {
        if (mapping is not IReadOnlyDictionary<string, object> dictionary)
        {
            return fallback;
        }
        return dictionary.TryGetValue(key, out var value) ? value : fallback;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        for (var index = 0; index < text.Length;)
        {
            var character = text[index];
            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }
            if (character == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                index += 2;
                while (index < text.Length && text[index] is not '\r' and not '\n')
                {
                    index++;
                }
                continue;
            }
            if (character is '{' or '}')
            {
                tokens.Add(character.ToString());
                index++;
                continue;
            }
            if (character == '"')
            {
                index++;
                var value = new StringBuilder();
                var closed = false;
                while (index < text.Length)
                {
                    character = text[index];
                    if (character == '"')
                    {
                        index++;
                        closed = true;
                        break;
                    }
                    if (character == '\\' && index + 1 < text.Length
                        && text[index + 1] is '"' or '\\')
                    {
                        value.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }
                    value.Append(character);
                    index++;
                }
                if (!closed)
                {
                    throw new InvalidDataException("VDF 包含未闭合的字符串");
                }
                tokens.Add(value.ToString());
                continue;
            }

            var start = index;
            while (index < text.Length
                && !char.IsWhiteSpace(text[index])
                && text[index] is not '{' and not '}' and not '"')
            {
                index++;
            }
            tokens.Add(text[start..index]);
        }
        return tokens;
    }

    private static Dictionary<string, object> ParseObject(
        IReadOnlyList<string> tokens,
        ref int position,
        bool expectClosing)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (position < tokens.Count)
        {
            var token = tokens[position];
            if (token == "}")
            {
                if (!expectClosing)
                {
                    throw new InvalidDataException("VDF 包含多余的右括号");
                }
                position++;
                return result;
            }
            if (token == "{")
            {
                throw new InvalidDataException("VDF 对象缺少键");
            }
            var key = token;
            position++;
            if (position >= tokens.Count)
            {
                throw new InvalidDataException($"VDF 键 '{key}' 缺少值");
            }
            token = tokens[position++];
            if (token == "}")
            {
                throw new InvalidDataException($"VDF 键 '{key}' 缺少值");
            }
            result[key] = token == "{"
                ? ParseObject(tokens, ref position, true)
                : token;
        }
        if (expectClosing)
        {
            throw new InvalidDataException("VDF 包含未闭合的对象");
        }
        return result;
    }
}
