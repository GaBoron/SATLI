namespace Satli_Gui.Services;

internal static class ApplicationDataPaths
{
    private const string CurrentDirectoryName = "SATLI";
    private const string LegacyDirectoryName = "SteamAchievementTranslationInstaller";
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CurrentDirectoryName);
    internal static string WebViewUserDataDirectory => WebViewUserDataDirectoryFor(
        DefaultDataDirectory);

    internal static string WebViewUserDataDirectoryFor(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        return Path.Combine(applicationDataDirectory, "WebView2");
    }

    public static void MigrateDefaultDirectory() => MigrateDefaultDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string MigrateDefaultDirectory(string localAppData)
    {
        var current = Path.Combine(localAppData, CurrentDirectoryName);
        var legacy = Path.Combine(localAppData, LegacyDirectoryName);
        if (!Directory.Exists(legacy))
        {
            return current;
        }

        if (Directory.Exists(current))
        {
            if (Directory.EnumerateFileSystemEntries(current).Any())
            {
                throw new IOException(
                    $"无法迁移旧数据目录：目标目录已包含文件：{current}");
            }
            Directory.Delete(current);
        }

        Directory.Move(legacy, current);
        DeleteLegacyUpdatePackages(current);
        return current;
    }

    internal static string MigrateStoredDataDirectory(
        string storedDirectory,
        string? localAppData = null)
    {
        if (string.IsNullOrWhiteSpace(storedDirectory))
        {
            return storedDirectory;
        }

        localAppData ??= Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var legacy = Path.Combine(localAppData, LegacyDirectoryName);
        return Path.TrimEndingDirectorySeparator(storedDirectory).Equals(
            Path.TrimEndingDirectorySeparator(legacy),
            StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(localAppData, CurrentDirectoryName)
            : storedDirectory;
    }

    private static void DeleteLegacyUpdatePackages(string dataDirectory)
    {
        var updateDirectory = Path.Combine(dataDirectory, "updates");
        if (!Directory.Exists(updateDirectory))
        {
            return;
        }

        foreach (var pattern in new[]
                 {
                     "SATLInstaller-Setup-v*.exe",
                     "SATLInstaller-Setup-v*.exe.part",
                 })
        {
            foreach (var path in Directory.EnumerateFiles(updateDirectory, pattern))
            {
                File.Delete(path);
            }
        }
    }
}
