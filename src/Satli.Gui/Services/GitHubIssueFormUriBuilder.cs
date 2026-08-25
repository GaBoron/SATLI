namespace Satli_Gui.Services;

public static class GitHubIssueFormUriBuilder
{
    private const string IssueBase =
        "https://github.com/GaBoron/steam-achievement-translation-library/issues/new";

    public static Uri Build(
        string template,
        string title,
        IEnumerable<KeyValuePair<string, string?>> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var values = new List<KeyValuePair<string, string?>>
        {
            new("template", template),
            new("title", title),
        };
        values.AddRange(fields);

        var query = string.Join(
            "&",
            values
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key)
                    && !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return new Uri($"{IssueBase}?{query}");
    }
}
