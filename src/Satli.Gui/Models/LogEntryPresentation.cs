using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;

namespace Satli_Gui.Models;

public sealed class LogEntryPresentation
{
    public LogEntryPresentation()
    {
    }

    public LogEntryPresentation(
        DateTimeOffset timestamp,
        string level,
        string category,
        string message)
    {
        Timestamp = timestamp;
        Level = level;
        Category = category;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public TextWrapping MessageWrapping { get; set; } = TextWrapping.Wrap;
    public string TimestampText => Timestamp.ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    public string LevelGlyph => Level switch
    {
        "错误" => "\uEA39",
        "警告" => "\uE7BA",
        "调试" => "\uEBE8",
        "详细" => "\uE946",
        _ => "\uE946",
    };

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || Level.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Message.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || TimestampText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}

public static partial class LogEntryParser
{
    public static IReadOnlyList<LogEntryPresentation> Parse(string content)
    {
        var entries = new List<LogEntryPresentation>();
        foreach (var line in content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = LogLine().Match(line);
            if (!match.Success
                || !DateTimeOffset.TryParseExact(
                    match.Groups["timestamp"].Value,
                    "yyyy-MM-dd HH:mm:ss.fff zzz",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var timestamp))
            {
                continue;
            }

            entries.Add(new LogEntryPresentation(
                timestamp,
                match.Groups["level"].Value,
                match.Groups["category"].Value,
                match.Groups["message"].Value));
        }
        return entries;
    }

    [GeneratedRegex("^(?<timestamp>\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3} [+-]\\d{2}:\\d{2}) \\[(?<level>[^]]+)\\] \\[(?<category>[^]]+)\\] (?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LogLine();
}
