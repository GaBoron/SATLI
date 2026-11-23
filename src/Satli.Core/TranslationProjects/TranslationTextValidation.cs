using System.Text.RegularExpressions;
using Satli.Core.Formats;

namespace Satli.Core.TranslationProjects;

/// <summary>Shared checks for human and generated text; empty name and description are independent.</summary>
public static partial class TranslationTextValidation
{
    public static void Validate(string apiName, AchievementTranslation text, IEnumerable<AchievementTranslation> originals)
    {
        var sources = originals.ToArray();
        Check(apiName, "name", text.Name, sources.Select(value => value.Name).ToArray());
        Check(apiName, "description", text.Description, sources.Select(value => value.Description).ToArray());
    }

    private static void Check(string apiName, string field, string text, string[] sources)
    {
        if (text.EnumerateRunes().Count() > 65536 || text.Any(value => char.IsControl(value) && value is not ('\n' or '\r' or '\t')))
            throw new IntegrityException($"{apiName} · {field}：文本过长或含无效控制字符。");
        if (sources.Length == 0) return;
        var populated = sources.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (populated.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(text))
                throw new IntegrityException($"{apiName} · {field}：原文为空，请勿编造内容。");
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
            throw new IntegrityException($"{apiName} · {field}：缺少译文。");

        var tokens = Tokens(text);
        var expected = new List<string[]>();
        foreach (var source in populated)
        {
            var sourceTokens = Tokens(source);
            if (sourceTokens.SequenceEqual(tokens)) return;
            expected.Add(sourceTokens);
        }

        const int sourceLimit = 4;
        var alternatives = expected.Select(DescribeTokens).Distinct(StringComparer.Ordinal).Take(sourceLimit + 1).ToArray();
        var originals = string.Join(" 或 ", alternatives.Take(sourceLimit));
        if (alternatives.Length > sourceLimit) originals += " 或其他原文组";
        throw new IntegrityException($"{apiName} · {field}：占位符与原文不一致。"
            + $"原文占位符：{originals}。译文占位符：{DescribeTokens(tokens)}。");
    }

    private static string[] Tokens(string text) => Placeholder().Matches(text)
        .Where(match => !match.ValueSpan.SequenceEqual("%%") && !IsProsePercentage(text, match))
        .Select(match => match.Value)
        .Order(StringComparer.Ordinal).ToArray();

    private static bool IsProsePercentage(string text, Match match)
    {
        // In "90% of", the apparent conversion starts a word and the only printf flag is a space.
        var conversion = match.Groups["conversion"];
        var prefixStart = match.Index + 1;
        if (!conversion.Success || conversion.Index == prefixStart
            || !text.AsSpan(prefixStart, conversion.Index - prefixStart).Trim(' ').IsEmpty)
            return false;

        var end = match.Index + match.Length;
        if (end < text.Length && text[end] is '\'' or '’') end++;
        if (end == text.Length || !(char.IsLower(text[end]) || char.IsUpper(text[end]))) return false;

        var beforePercent = text.AsSpan(0, match.Index).TrimEnd();
        return !beforePercent.IsEmpty && char.IsDigit(beforePercent[^1]);
    }

    private static string DescribeTokens(string[] tokens)
    {
        if (tokens.Length == 0) return "无";

        const int tokenLimit = 6;
        const int lengthLimit = 48;
        var preview = tokens.Take(tokenLimit).Select(token =>
            (token.Length > lengthLimit ? token[..lengthLimit] + "…" : token)
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"));
        var remaining = tokens.Length > tokenLimit ? $", …（另有 {tokens.Length - tokenLimit} 项）" : "";
        return $"[{string.Join(", ", preview)}{remaining}]";
    }

    [GeneratedRegex(@"%%|%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.(?:\d+|\*))?"
        + @"(?<conversion>(?:hh|h|ll|l|L|z|j|t)?[diuoxXfFeEgGaAcspn])|\$?\{[a-zA-Z0-9_]+(?::[^{}]+)?\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
