using System.Text;
using System.Text.Json;
using Satli.Core.FileSystem;
using Satli_Gui.Models;
using Satli_Gui.Serialization;

namespace Satli_Gui.Services;

public sealed class SettingsService
{
    private static readonly byte[] ProxyPasswordEntropy =
        Encoding.UTF8.GetBytes("SATLI.ProxyPassword.v1");
    private static readonly byte[] SteamApiKeyEntropy =
        Encoding.UTF8.GetBytes("SATLI.SteamApiKey.v1");
    private static readonly byte[] LegacyProxyPasswordEntropy =
        Encoding.UTF8.GetBytes("SATLInstaller.ProxyPassword.v1");
    private static readonly byte[] LegacySteamApiKeyEntropy =
        Encoding.UTF8.GetBytes("SATLInstaller.SteamApiKey.v1");
    private readonly string _path;

    public static string DefaultDataDirectory => ApplicationDataPaths.DefaultDataDirectory;

    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(DefaultDataDirectory, "gui-settings.json");
    }

    public string SettingsPath => _path;

    public async Task<GuiSettings> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return new GuiSettings();
        }

        try
        {
            GuiSettings settings;
            await using (var stream = File.OpenRead(_path))
            {
                settings = await JsonSerializer.DeserializeAsync(
                    stream,
                    SatliJsonSerializerContext.Default.GuiSettings) ?? new GuiSettings();
            }
            var requiresRewrite = false;
            var migratedDataDirectory = ApplicationDataPaths.MigrateStoredDataDirectory(
                settings.DataDirectory);
            requiresRewrite |= !string.Equals(
                settings.DataDirectory,
                migratedDataDirectory,
                StringComparison.Ordinal);
            settings.DataDirectory = migratedDataDirectory;
            var material = WindowMaterialService.Normalize(settings.Material);
            requiresRewrite |= !string.Equals(settings.Material, material, StringComparison.Ordinal);
            settings.Material = material;
            settings.LogLevel = PersistentLogLevel(settings.LogLevel);
            settings.Network = LoadNetworkSettings(settings.Network, ref requiresRewrite);
            settings.DownloadSources = DownloadSourceCatalog.Normalize(settings.DownloadSources);
            settings.SteamLibrary = LoadSteamLibrarySettings(
                settings.SteamLibrary,
                ref requiresRewrite);
            if (requiresRewrite)
            {
                await SaveAsync(settings);
            }
            return settings;
        }
        catch (JsonException)
        {
            return new GuiSettings();
        }
        catch (IOException)
        {
            return new GuiSettings();
        }
    }

    public async Task SaveAsync(GuiSettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            string protectedSteamApiKey;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var network = NetworkSettingsValidator.Normalize(settings.Network);
                var downloadSources = DownloadSourceCatalog.Normalize(settings.DownloadSources);
                var steamLibrary = SteamLibrarySettingsValidator.Normalize(settings.SteamLibrary);
                protectedSteamApiKey = steamLibrary.ApiKeyChanged
                    || !string.IsNullOrEmpty(steamLibrary.ApiKey)
                        ? ProtectedDataMigration.Protect(
                            steamLibrary.ApiKey,
                            SteamApiKeyEntropy)
                        : steamLibrary.ProtectedApiKey;
                var persistentSettings = new GuiSettings
                {
                    SteamDirectory = settings.SteamDirectory,
                    DataDirectory = settings.DataDirectory,
                    Offline = settings.Offline,
                    Theme = settings.Theme,
                    Material = WindowMaterialService.Normalize(settings.Material),
                    LoggingEnabled = settings.LoggingEnabled,
                    LogLevel = PersistentLogLevel(settings.LogLevel),
                    LogRetentionDays = settings.LogRetentionDays,
                    LogWordWrap = settings.LogWordWrap,
                    CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup,
                    Network = new NetworkSettings
                    {
                        DnsMode = network.DnsMode,
                        DnsServers = network.DnsServers,
                        ProxyMode = network.ProxyMode,
                        ProxyAddress = network.ProxyAddress,
                        ProxyUsername = network.ProxyUsername,
                        ProtectedProxyPassword = ProtectedDataMigration.Protect(
                            network.ProxyPassword,
                            ProxyPasswordEntropy),
                    },
                    DownloadSources = downloadSources,
                    SteamLibrary = new SteamLibrarySettings
                    {
                        Enabled = steamLibrary.Enabled,
                        SteamId = steamLibrary.SteamId,
                        ProtectedApiKey = protectedSteamApiKey,
                    },
                };
                await JsonSerializer.SerializeAsync(
                    stream,
                    persistentSettings,
                    SatliJsonSerializerContext.Default.GuiSettings);
                await stream.FlushAsync();
            }
            File.Move(temporary, _path, true);
            settings.SteamLibrary.ProtectedApiKey = protectedSteamApiKey;
            settings.SteamLibrary.ApiKeyChanged = false;
        }
        finally
        {
            RecycleBin.FileIfExists(temporary);
        }
    }

    private static string PersistentLogLevel(string level) => level switch
    {
        "detailed" => "detailed",
        "debug" => "detailed",
        _ => "standard",
    };

    private static NetworkSettings LoadNetworkSettings(
        NetworkSettings? stored,
        ref bool requiresRewrite)
    {
        stored ??= new NetworkSettings();
        try
        {
            var result = ProtectedDataMigration.Unprotect(
                stored.ProtectedProxyPassword,
                ProxyPasswordEntropy,
                LegacyProxyPasswordEntropy);
            stored.ProxyPassword = result.Value;
            requiresRewrite |= result.RequiresRewrite;
            return NetworkSettingsValidator.Normalize(stored);
        }
        catch (ArgumentException)
        {
            return new NetworkSettings();
        }
    }

    private static SteamLibrarySettings LoadSteamLibrarySettings(
        SteamLibrarySettings? stored,
        ref bool requiresRewrite)
    {
        stored ??= new SteamLibrarySettings();
        var result = ProtectedDataMigration.Unprotect(
            stored.ProtectedApiKey,
            SteamApiKeyEntropy,
            LegacySteamApiKeyEntropy);
        stored.ApiKey = result.Value;
        requiresRewrite |= result.RequiresRewrite;
        return SteamLibrarySettingsValidator.Normalize(stored);
    }
}
