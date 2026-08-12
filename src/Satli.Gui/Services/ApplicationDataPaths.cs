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

    internal static string MigrateDefaultDirectory(
        string localAppData,
        Action<string, string>? moveDirectory = null)
    {
        var current = Path.Combine(localAppData, CurrentDirectoryName);
        var legacy = Path.Combine(localAppData, LegacyDirectoryName);
        if (!Directory.Exists(legacy))
        {
            return current;
        }

        var shouldTryMove = !Directory.Exists(current)
            || !Directory.EnumerateFileSystemEntries(current).Any();
        if (shouldTryMove)
        {
            try
            {
                if (Directory.Exists(current))
                {
                    Directory.Delete(current);
                }
                (moveDirectory ?? Directory.Move)(legacy, current);
                DeleteLegacyUpdatePackages(current);
                return current;
            }
            catch (IOException)
            {
                // MSIX can reject moving an AppData directory across its
                // virtualized view. Fall back to a non-destructive copy.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the legacy directory and copy what is accessible.
            }
        }

        CopyDirectoryContents(legacy, current);
        DeleteLegacyUpdatePackages(current);
        return current;
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var directory in Directory.EnumerateDirectories(source, "*", options))
        {
            Directory.CreateDirectory(Path.Combine(
                destination,
                Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", options))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }
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
