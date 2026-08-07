using System.Text;
using System.Text.Json;

namespace Satli_Gui.Services;

public sealed record GitHubCredential(
    string Login,
    string AvatarUrl,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class GitHubCredentialStore
{
    private static readonly byte[] AccessEntropy =
        Encoding.UTF8.GetBytes("SATLI.GitHubAccessToken.v1");
    private static readonly byte[] RefreshEntropy =
        Encoding.UTF8.GetBytes("SATLI.GitHubRefreshToken.v1");
    private static readonly byte[] LegacyAccessEntropy =
        Encoding.UTF8.GetBytes("SATLInstaller.GitHubAccessToken.v1");
    private static readonly byte[] LegacyRefreshEntropy =
        Encoding.UTF8.GetBytes("SATLInstaller.GitHubRefreshToken.v1");
    private readonly string _path;

    public GitHubCredentialStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            ApplicationDataPaths.DefaultDataDirectory,
            "github-auth.json");
    }

    public async Task<GitHubCredential?> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return null;
        }
        try
        {
            var json = await File.ReadAllTextAsync(_path);
            var stored = JsonSerializer.Deserialize<StoredCredential>(json);
            if (stored is null || string.IsNullOrWhiteSpace(stored.Login))
            {
                return null;
            }
            var access = ProtectedDataMigration.Unprotect(
                stored.ProtectedAccessToken,
                AccessEntropy,
                LegacyAccessEntropy);
            var refresh = ProtectedDataMigration.Unprotect(
                stored.ProtectedRefreshToken,
                RefreshEntropy,
                LegacyRefreshEntropy);
            var credential = new GitHubCredential(
                stored.Login,
                stored.AvatarUrl ?? string.Empty,
                access.Value,
                stored.AccessTokenExpiresAt,
                refresh.Value,
                stored.RefreshTokenExpiresAt);
            if (access.RequiresRewrite || refresh.RequiresRewrite)
            {
                await SaveAsync(credential);
            }
            return credential;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(GitHubCredential credential)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var stored = new StoredCredential
            {
                Login = credential.Login,
                AvatarUrl = credential.AvatarUrl,
                ProtectedAccessToken = ProtectedDataMigration.Protect(
                    credential.AccessToken,
                    AccessEntropy),
                AccessTokenExpiresAt = credential.AccessTokenExpiresAt,
                ProtectedRefreshToken = ProtectedDataMigration.Protect(
                    credential.RefreshToken,
                    RefreshEntropy),
                RefreshTokenExpiresAt = credential.RefreshTokenExpiresAt,
            };
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private sealed class StoredCredential
    {
        public string Login { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string ProtectedAccessToken { get; set; } = string.Empty;
        public DateTimeOffset AccessTokenExpiresAt { get; set; }
        public string ProtectedRefreshToken { get; set; } = string.Empty;
        public DateTimeOffset RefreshTokenExpiresAt { get; set; }
    }
}
