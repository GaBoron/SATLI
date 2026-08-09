using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Satli_Gui.Services;

public sealed record SteamSchemaChange(
    string AppId,
    WatcherChangeTypes ChangeType,
    string Path);

public sealed partial class SteamSchemaChangeMonitor : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(700);
    private readonly ConcurrentDictionary<string, PendingChange> _pending = new();
    private readonly ConcurrentDictionary<string, int> _activeSuppressions = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _suppressedUntil = new();
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public SteamSchemaChangeMonitor()
    {
        _timer = new Timer(FlushPending, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public event EventHandler<SteamSchemaChange>? SchemaChanged;

    public string WatchedDirectory { get; private set; } = string.Empty;

    public void Configure(string? steamDirectory)
    {
        ThrowIfDisposed();
        var statsDirectory = string.IsNullOrWhiteSpace(steamDirectory)
            ? string.Empty
            : Path.GetFullPath(Path.Combine(steamDirectory, "appcache", "stats"));
        if (string.Equals(statsDirectory, WatchedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisposeWatcher();
        WatchedDirectory = statsDirectory;
        if (string.IsNullOrWhiteSpace(statsDirectory) || !Directory.Exists(statsDirectory))
        {
            return;
        }

        _watcher = new FileSystemWatcher(statsDirectory, "UserGameStatsSchema_*.bin")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime
                | NotifyFilters.Attributes,
        };
        _watcher.Changed += Watcher_Changed;
        _watcher.Created += Watcher_Changed;
        _watcher.Deleted += Watcher_Changed;
        _watcher.Renamed += Watcher_Renamed;
        _watcher.EnableRaisingEvents = true;
    }

    public IDisposable BeginSuppression(IEnumerable<string> appIds, TimeSpan? cooldown = null)
    {
        ThrowIfDisposed();
        var suppressedAppIds = appIds
            .Where(appId => !string.IsNullOrWhiteSpace(appId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var appId in suppressedAppIds)
        {
            _activeSuppressions.AddOrUpdate(appId, 1, (_, count) => count + 1);
            _pending.TryRemove(appId, out _);
        }
        return new SuppressionLease(this, suppressedAppIds, cooldown ?? TimeSpan.FromSeconds(3));
    }

    public static bool TryGetAppId(string? path, out string appId)
    {
        var match = SchemaFileNameRegex().Match(Path.GetFileName(path ?? string.Empty));
        appId = match.Success ? match.Groups[1].Value : string.Empty;
        return match.Success;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DisposeWatcher();
        _timer.Dispose();
        _pending.Clear();
        _activeSuppressions.Clear();
        _suppressedUntil.Clear();
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs args) => Queue(args.FullPath, args.ChangeType);

    private void Watcher_Renamed(object sender, RenamedEventArgs args)
    {
        Queue(args.OldFullPath, WatcherChangeTypes.Renamed);
        Queue(args.FullPath, WatcherChangeTypes.Renamed);
    }

    private void Queue(string path, WatcherChangeTypes changeType)
    {
        if (!TryGetAppId(path, out var appId))
        {
            return;
        }
        var now = DateTimeOffset.UtcNow;
        if (_activeSuppressions.ContainsKey(appId))
        {
            _pending.TryRemove(appId, out _);
            return;
        }
        if (_suppressedUntil.TryGetValue(appId, out var until) && until > now)
        {
            return;
        }
        _suppressedUntil.TryRemove(appId, out _);
        _pending[appId] = new PendingChange(
            new SteamSchemaChange(appId, changeType, path),
            now + DebounceDelay);
    }

    private void FlushPending(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _pending)
        {
            if (item.Value.DueAt > now || !_pending.TryRemove(item.Key, out var pending))
            {
                continue;
            }
            if (_suppressedUntil.TryGetValue(item.Key, out var until) && until > now)
            {
                continue;
            }
            SchemaChanged?.Invoke(this, pending.Change);
        }
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= Watcher_Changed;
        _watcher.Created -= Watcher_Changed;
        _watcher.Deleted -= Watcher_Changed;
        _watcher.Renamed -= Watcher_Renamed;
        _watcher.Dispose();
        _watcher = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void EndSuppression(IReadOnlyList<string> appIds, TimeSpan cooldown)
    {
        var until = DateTimeOffset.UtcNow + cooldown;
        foreach (var appId in appIds)
        {
            while (_activeSuppressions.TryGetValue(appId, out var count))
            {
                if (count > 1 && _activeSuppressions.TryUpdate(appId, count - 1, count))
                {
                    break;
                }
                if (count == 1 && _activeSuppressions.TryRemove(
                        new KeyValuePair<string, int>(appId, count)))
                {
                    break;
                }
            }
            _suppressedUntil.AddOrUpdate(
                appId,
                until,
                (_, existing) => existing > until ? existing : until);
            _pending.TryRemove(appId, out _);
        }
    }

    private sealed record PendingChange(SteamSchemaChange Change, DateTimeOffset DueAt);

    private sealed class SuppressionLease(
        SteamSchemaChangeMonitor owner,
        IReadOnlyList<string> appIds,
        TimeSpan cooldown) : IDisposable
    {
        private SteamSchemaChangeMonitor? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.EndSuppression(appIds, cooldown);
    }

    [GeneratedRegex(@"^UserGameStatsSchema_([1-9][0-9]{0,19})\.bin$", RegexOptions.IgnoreCase)]
    private static partial Regex SchemaFileNameRegex();
}
