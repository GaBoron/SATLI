namespace Satli_Gui.Services;

public static class GitHubIssueFormUriBuilder
{
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

        return GitHubIssueUriBuilder.Build(values);
    }
}
