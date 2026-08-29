using Satli_Gui.Models;

namespace Satli_Gui.Services;

internal sealed record GameInventoryPreloadAttempt(
    GameInventorySnapshot? Snapshot,
    Exception? Error);

internal sealed class GameInventoryPreloader
{
    private const int MaximumParallelPreloads = 2;
    private readonly IGameInventoryLoader _loader;
    private readonly LogService _logs;
    private readonly SemaphoreSlim _parallelGate = new(MaximumParallelPreloads);
    private readonly Dictionary<GameInventoryScope, Task<GameInventoryPreloadAttempt>> _tasks = [];
    private readonly object _sync = new();
    private bool _started;

    public GameInventoryPreloader(
        IGameInventoryLoader? loader = null,
        LogService? logs = null)
    {
        _loader = loader ?? new GameInventoryLoader();
        _logs = logs ?? new LogService();
    }

    public void Start(GuiSettings settings)
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }
            _started = true;
            foreach (var scope in new[] { GameInventoryScope.Local, GameInventoryScope.Cloud })
            {
                _tasks[scope] = Task.Run(() => PreloadAsync(scope, settings));
            }
        }
    }

    public Task<GameInventoryPreloadAttempt>? Take(GameInventoryScope scope)
    {
        lock (_sync)
        {
            if (!_tasks.Remove(scope, out var task))
            {
                return null;
            }
            return task;
        }
    }

    private async Task<GameInventoryPreloadAttempt> PreloadAsync(
        GameInventoryScope scope,
        GuiSettings settings)
    {
        await _parallelGate.WaitAsync();
        try
        {
            await _logs.WriteAsync(
                "信息",
                "后台预加载",
                $"开始预加载 {ScopeLabel(scope)}游戏清单。");
            var snapshot = await _loader.LoadAsync(
                scope,
                settings,
                useCatalogCache: true);
            await _logs.WriteAsync(
                "信息",
                "后台预加载",
                $"{ScopeLabel(scope)}游戏清单预加载完成，共 {snapshot.Games.Count} 项。");
            await _logs.WriteAsync(
                "详细",
                "后台预加载",
                $"范围={scope}；数量={snapshot.Games.Count}；耗时={snapshot.ElapsedMilliseconds} ms；" +
                $"并发上限={MaximumParallelPreloads}。",
                detailed: true);
            return new GameInventoryPreloadAttempt(snapshot, null);
        }
        catch (Exception exception)
        {
            await _logs.WriteAsync(
                "警告",
                "后台预加载",
                $"{ScopeLabel(scope)}游戏清单预加载失败；打开页面时将重新加载。");
            await _logs.WriteAsync(
                "调试",
                "后台预加载",
                $"范围={scope}；异常={exception}",
                debug: true);
            return new GameInventoryPreloadAttempt(null, exception);
        }
        finally
        {
            _parallelGate.Release();
        }
    }

    private static string ScopeLabel(GameInventoryScope scope) =>
        scope == GameInventoryScope.Local ? "本地" : "云端";
}
