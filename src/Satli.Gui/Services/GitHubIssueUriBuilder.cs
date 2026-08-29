namespace Satli_Gui.Services;

public static class GitHubIssueUriBuilder
{
    private const string IssueBase =
        "https://github.com/GaBoron/steam-achievement-translation-library/issues/new";

    public static Uri Build(IEnumerable<KeyValuePair<string, string?>> fields)
    {
        var query = string.Join(
            "&",
            fields
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key)
                    && !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return new Uri($"{IssueBase}?{query}");
    }
}
