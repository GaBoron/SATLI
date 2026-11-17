namespace Satli.Core.SatliLite;

/// <summary>Official release metadata used when unauthenticated GitHub API access is unavailable.</summary>
internal static class ComponentReleaseFallbacks
{
    // Published asset metadata from the official release endpoints. Keep each tuple together when updating.
    public static readonly OfficialReleaseAsset Millennium = new(
        "v3.6.3", "millennium-v3.6.3-windows-x86_64.zip", 4059400,
        "d0363f4aa5bfa44aecf4643fe5af8fc35055d126a940fb46ef9158a4c261463d",
        "https://github.com/SteamClientHomebrew/Millennium/releases/download/v3.6.3/millennium-v3.6.3-windows-x86_64.zip");

    public static readonly OfficialReleaseAsset Lite = new(
        "v0.3.0", "satli-lite.star", 103491,
        "cb19ca3c5dbdafd07725bb200d9a5b891c4a2a100966cbbb5f7c5c5f16af765c",
        "https://github.com/GaBoron/SATLI-lite/releases/download/v0.3.0/satli-lite.star");
}
