using Windows.Services.Store;

namespace Satli_Gui.Services;

internal sealed record StorePackageUpdateInfo(Version Version);

internal interface IStorePackageUpdateSource
{
    Task<IReadOnlyList<StorePackageUpdateInfo>> GetAvailableUpdatesAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class WindowsStorePackageUpdateSource : IStorePackageUpdateSource
{
    private readonly Func<nint> _windowHandleProvider;
    private StoreContext? _context;

    public WindowsStorePackageUpdateSource(Func<nint> windowHandleProvider)
    {
        _windowHandleProvider = windowHandleProvider;
    }

    public async Task<IReadOnlyList<StorePackageUpdateInfo>> GetAvailableUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _context ??= CreateContext();
        var updates = await _context.GetAppAndOptionalStorePackageUpdatesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return updates
            .Select(update => update.Package.Id.Version)
            .Select(version => new StorePackageUpdateInfo(new Version(
                version.Major,
                version.Minor,
                version.Build,
                version.Revision)))
            .ToArray();
    }

    private StoreContext CreateContext()
    {
        var context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, _windowHandleProvider());
        return context;
    }
}

internal sealed class StoreUpdateService
{
    public const string ProductId = "9PB7V9S03K80";
    public static Uri ProductPageUri { get; } = new(
        $"ms-windows-store://pdp/?ProductId={ProductId}");

    private readonly IStorePackageUpdateSource _source;
    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _releaseMetadataProvider;
    private readonly Version _currentVersion;

    public StoreUpdateService(UpdateService releaseMetadataService)
        : this(
            new WindowsStorePackageUpdateSource(() => App.WindowHandle),
            releaseMetadataService.CheckAsync,
            UpdateService.CurrentVersion)
    {
    }

    internal StoreUpdateService(
        IStorePackageUpdateSource source,
        Func<CancellationToken, Task<UpdateCheckResult>> releaseMetadataProvider,
        Version currentVersion)
    {
        _source = source;
        _releaseMetadataProvider = releaseMetadataProvider;
        _currentVersion = currentVersion;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var updates = await _source.GetAvailableUpdatesAsync(cancellationToken);
        var currentText = UpdateService.FormatVersion(_currentVersion);
        if (updates.Count == 0)
        {
            return new UpdateCheckResult(
                false,
                currentText,
                currentText,
                ProductPageUri,
                null,
                string.Empty,
                $"当前已是 Microsoft Store 提供的最新版本 v{currentText}。",
                IsMicrosoftStoreUpdate: true);
        }

        var normalizedCurrentVersion = UpdateService.Normalize(_currentVersion);
        var storeVersion = updates
            .Select(update => update.Version)
            .Select(UpdateService.Normalize)
            .Where(version => version > normalizedCurrentVersion)
            .Max();
        var release = await TryGetReleaseMetadataAsync(cancellationToken);
        var releaseVersion = TryGetNewerReleaseVersion(
            release,
            normalizedCurrentVersion);
        var latestVersion = storeVersion ?? releaseVersion;
        var latestText = latestVersion is null
            ? string.Empty
            : UpdateService.FormatVersion(latestVersion);
        var releaseNotes = release is not null
            && latestVersion is not null
            && releaseVersion == latestVersion
            && !string.IsNullOrWhiteSpace(release.ReleaseNotes)
                ? release.ReleaseNotes
                : MissingReleaseNotesMessage;
        return new UpdateCheckResult(
            true,
            currentText,
            latestText,
            ProductPageUri,
            null,
            releaseNotes,
            string.IsNullOrWhiteSpace(latestText)
                ? "Microsoft Store 中有可用更新。"
                : $"Microsoft Store 中有新版本 v{latestText}。",
            IsMicrosoftStoreUpdate: true);
    }

    private const string MissingReleaseNotesMessage =
        "已检测到 Microsoft Store 软件包更新。此版本的详细更新内容暂时无法读取，"
        + "请在 Microsoft Store 产品页查看。";

    private async Task<UpdateCheckResult?> TryGetReleaseMetadataAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _releaseMetadataProvider(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Store package discovery is the source of truth. Release notes are supplemental.
        }

        return null;
    }

    private static Version? TryGetNewerReleaseVersion(
        UpdateCheckResult? release,
        Version currentVersion)
    {
        if (release is null
            || !Version.TryParse(release.LatestVersion, out var parsed))
        {
            return null;
        }

        var normalized = UpdateService.Normalize(parsed);
        return normalized > currentVersion ? normalized : null;
    }
}
