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
        if (populated.Length == 0 && !string.IsNullOrWhiteSpace(text))
            throw new IntegrityException($"{apiName} · {field}：原文为空，请勿编造内容。");
        if (populated.Length > 0 && string.IsNullOrWhiteSpace(text))
            throw new IntegrityException($"{apiName} · {field}：缺少译文。");
        var tokens = Tokens(text);
        if (populated.Length > 0 && !populated.Any(source => Tokens(source).SequenceEqual(tokens)))
            throw new IntegrityException($"{apiName} · {field}：占位符与原文不一致。");
    }

    private static string[] Tokens(string text) => Placeholder().Matches(text).Select(match => match.Value)
        .Order(StringComparer.Ordinal).ToArray();

    [GeneratedRegex(@"%(?:\d+\$)?[-+#0 ]*(?:\d+|\*)?(?:\.(?:\d+|\*))?(?:hh|h|ll|l|L|z|j|t)?[diuoxXfFeEgGaAcspn]|\$?\{[a-zA-Z0-9_]+(?::[^{}]+)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
