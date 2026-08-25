using Microsoft.VisualBasic.FileIO;

namespace Satli_Gui.Services;

internal sealed record LegacyGitHubCredentialCleanupResult(
    IReadOnlyList<string> RecycledFiles,
    IReadOnlyList<string> Failures);

internal sealed class LegacyGitHubCredentialCleanupService
{
    private const string CredentialFileName = "github-auth.json";
    private readonly IReadOnlyList<string> _credentialPaths;
    private readonly Action<string> _recycleFile;

    internal LegacyGitHubCredentialCleanupService(
        IEnumerable<string>? credentialPaths = null,
        Action<string>? recycleFile = null)
    {
        _credentialPaths = (credentialPaths ?? GetDefaultCredentialPaths())
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _recycleFile = recycleFile ?? RecycleFile;
    }

    internal LegacyGitHubCredentialCleanupResult Cleanup()
    {
        var recycledFiles = new List<string>();
        var failures = new List<string>();

        foreach (var credentialPath in _credentialPaths)
        {
            if (!File.Exists(credentialPath))
            {
                continue;
            }

            try
            {
                _recycleFile(credentialPath);
                recycledFiles.Add(credentialPath);
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"无法将旧 GitHub 本地凭据移入回收站：{exception.Message}");
            }
        }

        return new LegacyGitHubCredentialCleanupResult(recycledFiles, failures);
    }

    private static IEnumerable<string> GetDefaultCredentialPaths()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)
            || !Path.IsPathFullyQualified(localAppData))
        {
            yield break;
        }
        yield return Path.Combine(localAppData, "SATLI", CredentialFileName);
        yield return Path.Combine(
            localAppData,
            "SteamAchievementTranslationInstaller",
            CredentialFileName);
    }

    private static void RecycleFile(string path)
    {
        FileSystem.DeleteFile(
            path,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin,
            UICancelOption.ThrowException);
    }
}
