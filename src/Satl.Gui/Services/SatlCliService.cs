using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed class SatlCliService
{
    private readonly CliProcessRunner _processRunner = new();
    private readonly ElevatedCliRunner _elevatedRunner = new();

    public async Task<CliRunResult> RunAsync(
        IEnumerable<string> arguments,
        Action<SatlEvent>? onEvent = null,
        Action<string>? onDiagnostic = null,
        NetworkSettings? networkSettings = null,
        SteamLibrarySettings? steamLibrarySettings = null,
        DownloadSourceSettings? downloadSourceSettings = null)
    {
        var argumentList = arguments.ToList();
        onDiagnostic?.Invoke($"请求参数={FormatArguments(argumentList)}");
        var invocation = new CliInvocation(
            argumentList,
            BuildEnvironment(
                argumentList,
                networkSettings,
                steamLibrarySettings,
                downloadSourceSettings));

        if (CliElevationPolicy.RequiresElevation(argumentList)
            && !ElevatedCliRunner.IsCurrentProcessElevated())
        {
            return await _elevatedRunner.RunAsync(invocation, onEvent, onDiagnostic);
        }

        return await _processRunner.RunAsync(invocation, onEvent, onDiagnostic);
    }

    public static SatlEvent ParseEvent(string line) => CliProcessRunner.ParseEvent(line);

    private static Dictionary<string, string> BuildEnvironment(
        IReadOnlyList<string> arguments,
        NetworkSettings? rawNetworkSettings,
        SteamLibrarySettings? rawSteamLibrarySettings,
        DownloadSourceSettings? rawDownloadSourceSettings)
    {
        var network = NetworkSettingsValidator.Normalize(rawNetworkSettings);
        var downloads = DownloadSourceCatalog.Normalize(rawDownloadSourceSettings);
        var environment = new Dictionary<string, string>
        {
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
            ["SATL_DNS_MODE"] = network.DnsMode,
            ["SATL_DNS_SERVERS"] = network.DnsServers,
            ["SATL_PROXY_MODE"] = network.ProxyMode,
            ["SATL_PROXY_ADDRESS"] = network.ProxyAddress,
            ["SATL_PROXY_USERNAME"] = network.ProxyUsername,
            ["SATL_PROXY_PASSWORD"] = network.ProxyPassword,
            ["SATL_INDEX_SOURCES"] = DownloadSourceCatalog.EnvironmentOrder(downloads.IndexSourceOrder),
            ["SATL_FILE_SOURCES"] = DownloadSourceCatalog.EnvironmentOrder(downloads.FileSourceOrder),
        };

        if (arguments.Contains("--include-owned-games", StringComparer.Ordinal))
        {
            var steamLibrary = SteamLibrarySettingsValidator.Normalize(rawSteamLibrarySettings);
            if (steamLibrary.Enabled && !string.IsNullOrEmpty(steamLibrary.ApiKey))
            {
                environment["SATL_STEAM_WEB_API_KEY"] = steamLibrary.ApiKey;
            }
        }
        return environment;
    }

    private static string FormatArguments(IEnumerable<string> arguments) =>
        string.Join(
            " ",
            arguments.Select(argument => System.Text.Json.JsonSerializer.Serialize(argument)));
}
