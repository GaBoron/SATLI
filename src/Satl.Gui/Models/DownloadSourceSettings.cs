namespace Satl_Gui.Models;

public static class DownloadSourceDefaults
{
    public static readonly IReadOnlyList<string> IndexOrder =
        ["jsdelivr", "github", "jsdelivr-fastly", "staticdelivr"];

    public static readonly IReadOnlyList<string> FileOrder =
        ["jsdelivr", "jsdelivr-fastly", "github"];
}

public sealed class DownloadSourceSettings
{
    public List<string> IndexSourceOrder { get; set; } = [.. DownloadSourceDefaults.IndexOrder];
    public List<string> FileSourceOrder { get; set; } = [.. DownloadSourceDefaults.FileOrder];
}

public sealed class DownloadSourceOption
{
    public DownloadSourceOption()
    {
    }

    public DownloadSourceOption(string id, string displayName, string description)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
    }

    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
