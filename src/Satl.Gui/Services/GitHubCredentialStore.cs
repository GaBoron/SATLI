using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Satl_Gui.Services;

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
        Encoding.UTF8.GetBytes("SATLInstaller.GitHubAccessToken.v1");
    private static readonly byte[] RefreshEntropy =
        Encoding.UTF8.GetBytes("SATLInstaller.GitHubRefreshToken.v1");
    private readonly string _path;

    public GitHubCredentialStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTranslationInstaller",
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
            return new GitHubCredential(
                stored.Login,
                stored.AvatarUrl ?? string.Empty,
                Unprotect(stored.ProtectedAccessToken, AccessEntropy),
                stored.AccessTokenExpiresAt,
                Unprotect(stored.ProtectedRefreshToken, RefreshEntropy),
                stored.RefreshTokenExpiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
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
                ProtectedAccessToken = Protect(credential.AccessToken, AccessEntropy),
                AccessTokenExpiresAt = credential.AccessTokenExpiresAt,
                ProtectedRefreshToken = Protect(credential.RefreshToken, RefreshEntropy),
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

    private static string Protect(string value, byte[] entropy)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            entropy,
            DataProtectionScope.CurrentUser));
    }

    private static string Unprotect(string? value, byte[] entropy)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(value),
            entropy,
            DataProtectionScope.CurrentUser));
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
