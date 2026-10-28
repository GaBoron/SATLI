namespace Satli.Core.SatliLite;

public static class MillenniumInstallation
{
    public static bool IsInstalled(string steamDirectory)
    {
        if (string.IsNullOrWhiteSpace(steamDirectory)) return false;
        try
        {
            var steam = Path.GetFullPath(steamDirectory);
            return File.Exists(Path.Combine(steam, "millennium", "lib", "millennium.dll"))
                || File.Exists(Path.Combine(steam, "millennium.dll"));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
