using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Satli.Core.Formats;
using Satli.Core.Models;

namespace Satli.Core.Steam;

public static partial class SteamLocator
{
    private const long SteamIdBase = 76561197960265728;

    public static string FindSteamDirectory(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return ValidateSteamDirectory(explicitPath);
        }

        foreach (var candidate in RegistryCandidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate)
                && File.Exists(Path.Combine(candidate, "steam.exe")))
            {
                return Path.GetFullPath(candidate);
            }
        }
        var candidates = new List<string>();
        foreach (var variable in new[] { "PROGRAMFILES(X86)", "PROGRAMFILES" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } root)
            {
                candidates.Add(Path.Combine(root, "Steam"));
            }
        }
        candidates.AddRange("CDEFG".Select(drive => $"{drive}:\\Steam"));
        var found = candidates.FirstOrDefault(candidate =>
            File.Exists(Path.Combine(candidate, "steam.exe")));
        return found is not null
            ? Path.GetFullPath(found)
            : throw new PreflightException("未检测到 Steam，请使用 --steam-dir 指定安装目录");
    }

    public static string ValidateSteamDirectory(string path)
    {
        var resolved = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        return File.Exists(Path.Combine(resolved, "steam.exe"))
            ? resolved
            : throw new PreflightException($"Steam 目录无效，未找到 steam.exe：{resolved}");
    }

    public static bool IsSteamRunning()
    {
        try
        {
            return Process.GetProcessesByName("steam").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<string> DiscoverLibraryDirectories(string steamDirectory)
    {
        var paths = new List<string> { Path.GetFullPath(steamDirectory) };
        var libraryFile = Path.Combine(steamDirectory, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            var data = VdfParser.Load(libraryFile);
            var root = VdfParser.Get(data, "libraryfolders", data);
            if (root is IReadOnlyDictionary<string, object> dictionary)
            {
                foreach (var (key, value) in dictionary)
                {
                    if (!key.All(char.IsAsciiDigit))
                    {
                        continue;
                    }
                    var rawPath = value is IReadOnlyDictionary<string, object>
                        ? VdfParser.Get(value, "path")
                        : value;
                    if (rawPath is string text && !string.IsNullOrWhiteSpace(text))
                    {
                        paths.Add(text);
                    }
                }
            }
        }
        return paths.Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static Dictionary<string, string> DiscoverInstalledGames(string steamDirectory)
    {
        var games = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var library in DiscoverLibraryDirectories(steamDirectory))
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }
            try
            {
                foreach (var path in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
                {
                    var match = AppManifestRegex().Match(Path.GetFileName(path));
                    if (!match.Success)
                    {
                        continue;
                    }
                    var name = string.Empty;
                    try
                    {
                        var manifest = VdfParser.Load(path);
                        var state = VdfParser.Get(manifest, "AppState", manifest);
                        name = Convert.ToString(VdfParser.Get(state, "name", string.Empty))?.Trim()
                            ?? string.Empty;
                    }
                    catch (PreflightException)
                    {
                    }
                    games[match.Groups[1].Value] = name;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new PreflightException($"无法读取 Steam 库目录：{steamApps}：{exception.Message}", exception);
            }
        }
        return games;
    }

    public static IReadOnlyList<SteamAccount> DiscoverAccounts(string steamDirectory)
    {
        var path = Path.Combine(steamDirectory, "config", "loginusers.vdf");
        if (!File.Exists(path))
        {
            return [];
        }
        var data = VdfParser.Load(path);
        if (VdfParser.Get(data, "users", data) is not IReadOnlyDictionary<string, object> users)
        {
            return [];
        }
        var accounts = new List<SteamAccount>();
        foreach (var (steamId, raw) in users)
        {
            if (!steamId.All(char.IsAsciiDigit)
                || raw is not IReadOnlyDictionary<string, object>)
            {
                continue;
            }
            var accountName = Convert.ToString(VdfParser.Get(raw, "AccountName", string.Empty))
                ?? string.Empty;
            var personaName = Convert.ToString(VdfParser.Get(raw, "PersonaName", accountName))
                ?? accountName;
            var mostRecent = Convert.ToString(VdfParser.Get(raw, "MostRecent", "0")) == "1";
            accounts.Add(new SteamAccount(steamId, accountName, personaName, mostRecent));
        }
        return accounts.OrderBy(account => !account.MostRecent)
            .ThenBy(account => account.PersonaName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(account => account.SteamId, StringComparer.Ordinal)
            .ToArray();
    }

    public static HashSet<string> DiscoverAccountCachedApps(
        string steamDirectory,
        SteamAccount account)
    {
        var path = Path.Combine(
            steamDirectory,
            "userdata",
            SteamId32(account.SteamId),
            "config",
            "localconfig.vdf");
        if (!File.Exists(path))
        {
            return [];
        }
        object? data = VdfParser.Load(path);
        foreach (var key in new[] { "UserLocalConfigStore", "Software", "Valve", "Steam", "apps" })
        {
            data = VdfParser.Get(data, key, new Dictionary<string, object>());
        }
        return data is IReadOnlyDictionary<string, object> apps
            ? apps.Keys.Where(key => key.All(char.IsAsciiDigit)).ToHashSet(StringComparer.Ordinal)
            : [];
    }

    public static Dictionary<string, DiscoveryRecord> DiscoverLocalGames(
        string steamDirectory,
        string? accountId = null)
    {
        var records = new Dictionary<string, DiscoveryRecord>(StringComparer.Ordinal);
        foreach (var (appId, gameName) in DiscoverInstalledGames(steamDirectory))
        {
            var record = new DiscoveryRecord(appId, gameName);
            record.Discovery.Add("installed");
            records[appId] = record;
        }
        var accounts = DiscoverAccounts(steamDirectory);
        var selected = string.IsNullOrWhiteSpace(accountId)
            ? accounts
            : accounts.Where(account => account.SteamId == accountId).ToArray();
        if (!string.IsNullOrWhiteSpace(accountId) && selected.Count == 0)
        {
            throw new PreflightException($"本机没有 Steam 账号：{accountId}");
        }
        foreach (var account in selected)
        {
            foreach (var appId in DiscoverAccountCachedApps(steamDirectory, account))
            {
                if (!records.TryGetValue(appId, out var record))
                {
                    record = new DiscoveryRecord(appId);
                    records[appId] = record;
                }
                record.Discovery.Add("account-cache");
                record.Accounts.Add(account.SteamId);
            }
        }
        return records;
    }

    public static string SchemaTarget(string steamDirectory, string appId) =>
        appId.All(char.IsAsciiDigit)
            ? Path.Combine(steamDirectory, "appcache", "stats", $"UserGameStatsSchema_{appId}.bin")
            : throw new PreflightException($"无效的 Steam App ID：{appId}");

    public static IReadOnlyList<string> DetectAchievementLanguages(string schemaPath)
    {
        try
        {
            return Formats.BinaryKeyValues.Preview(File.ReadAllBytes(schemaPath)).Languages;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PreflightException)
        {
            return [];
        }
    }

    private static IEnumerable<string?> RegistryCandidates()
    {
        yield return ReadRegistry(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        yield return ReadRegistry(
            Registry.LocalMachine,
            @"SOFTWARE\WOW6432Node\Valve\Steam",
            "InstallPath");
        yield return ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
    }

    private static string? ReadRegistry(RegistryKey root, string keyName, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(keyName);
            return Convert.ToString(key?.GetValue(valueName));
        }
        catch
        {
            return null;
        }
    }

    private static string SteamId32(string steamId) =>
        long.TryParse(steamId, out var numeric)
            ? (numeric >= SteamIdBase ? numeric - SteamIdBase : numeric).ToString()
            : steamId;

    [GeneratedRegex("^appmanifest_([0-9]+)\\.acf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AppManifestRegex();
}
