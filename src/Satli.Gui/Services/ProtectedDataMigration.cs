using System.Security.Cryptography;
using System.Text;

namespace Satli_Gui.Services;

internal sealed record UnprotectedSecret(string Value, bool RequiresRewrite);

internal static class ProtectedDataMigration
{
    public static string Protect(string value, byte[] entropy)
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

    public static UnprotectedSecret Unprotect(
        string? protectedValue,
        byte[] currentEntropy,
        params byte[][] legacyEntropies)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return new UnprotectedSecret(string.Empty, false);
        }

        if (TryUnprotect(protectedValue, currentEntropy, out var current))
        {
            return new UnprotectedSecret(current, false);
        }
        foreach (var legacyEntropy in legacyEntropies)
        {
            if (TryUnprotect(protectedValue, legacyEntropy, out var legacy))
            {
                return new UnprotectedSecret(legacy, true);
            }
        }
        return new UnprotectedSecret(string.Empty, false);
    }

    private static bool TryUnprotect(
        string protectedValue,
        byte[] entropy,
        out string clearValue)
    {
        try
        {
            var clearBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedValue),
                entropy,
                DataProtectionScope.CurrentUser);
            clearValue = Encoding.UTF8.GetString(clearBytes);
            return true;
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException)
        {
            clearValue = string.Empty;
            return false;
        }
    }
}
