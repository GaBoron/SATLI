using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Satli.Core.FileSystem;
using Satli.Core.Formats;

namespace Satli.Core.SatliLite;

/// <summary>Uses only SATLI-lite's public v1 mailbox; never reads its private translation state.</summary>
public sealed class LiteClient
{
    private readonly string _steam;
    private readonly string _root;
    private readonly string _tracking;
    private readonly Func<bool> _steamRunning;
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private const int MaximumRequestBytes = 32 * 1024 * 1024 + 65536;
    public const string ClientId = "satli-desktop";

    public LiteClient(string steamDirectory, string dataDirectory, Func<bool>? steamRunning = null)
    {
        _steam = Path.TrimEndingDirectorySeparator(Path.GetFullPath(steamDirectory));
        _root = Path.Combine(_steam, "millennium", "config", "satli-lite", "external", "v1");
        _tracking = Path.Combine(Path.GetFullPath(dataDirectory), "lite", "requests");
        _steamRunning = steamRunning ?? IsSteamRunning;
    }

    public string PluginPath => Path.Combine(_steam, "millennium", "plugins", "satli-lite.star");
    public string SteamDirectory => _steam;

    public async Task<LiteConnection> InspectAsync(CancellationToken cancellationToken = default)
    {
        if (!MillenniumInstallation.IsInstalled(_steam))
            return new("millennium_missing", "未安装 Millennium，请按官方指南安装。", []);
        if (File.Exists(Path.Combine(_steam, "millennium", "plugins", "satli.star")))
            return new("legacy_bridge", "发现旧 SATLI 显示桥。可先选择已管理译文发送到 lite；移除旧桥后再启用 lite，原快照和备份会保留。", []);
        if (!File.Exists(PluginPath))
            return new("not_installed", "未安装 SATLI-lite。", []);
        var protocol = await ReadAsync(Path.Combine(_root, "protocol.json"), 65536, cancellationToken).ConfigureAwait(false);
        if (protocol is null)
            return new("uninitialized", "尚未取得协议：请在 Steam → Millennium → Plugins 启用 SATLI-lite，并至少启动一次，再重试。", []);
        if (!IsProtocolV1(protocol)
            || protocol["plugin_id"]?.ToString() != "com.gaboron.satli-lite"
            || protocol["operations"] is not JsonArray operations
            || !operations.Any(value => value?.ToString() == "import")
            || !operations.Any(value => value?.ToString() == "query_app"))
            return new("incompatible", "SATLI-lite 外部协议或能力不兼容，请升级插件。", []);
        var versions = protocol["translation_versions"] is JsonArray formats
            ? formats.Select(value => value is JsonValue number && number.TryGetValue<int>(out var version) ? version : 0).ToArray()
            : new[] { 1 };
        if ((protocol.ContainsKey("translation_versions") && protocol["translation_versions"] is not JsonArray)
            || versions.Length == 0 || versions.Any(version => version < 1))
            return new("incompatible", "SATLI-lite 未提供有效的翻译格式能力，请升级插件。", []);
        var pluginVersion = protocol["plugin_version"]?.ToString() ?? "";
        if (!_steamRunning())
            return new("steam_stopped", "Steam 未运行。可排队发送，启动 Steam 并启用 SATLI-lite 后处理。", versions, true, pluginVersion);
        var runtime = await ReadAsync(Path.Combine(_root, "runtime.json"), 65536, cancellationToken).ConfigureAwait(false);
        var heartbeat = long.TryParse(runtime?["heartbeat_at"]?.ToString(), out var time) ? time : 0;
        var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - heartbeat;
        if (runtime is null || !IsProtocolV1(runtime) || age is < 0 or > 15
            || !IsBoolean(runtime["backend_running"], true)
            || !IsBoolean(runtime["accepting_requests"], true))
            return new("unavailable", "Steam 正在运行，但 lite 没有有效心跳。可排队发送，请检查插件启用状态或重启 Steam 后刷新。", versions, true, pluginVersion);
        return new("connected", "已连接 SATLI-lite。", versions, true, pluginVersion);
    }

    public IReadOnlyList<string> KnownAppIds() => !Directory.Exists(_tracking) ? []
        : Directory.EnumerateFiles(_tracking, "*.json")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(value => value.Length is > 0 and <= 10 && value[0] != '0' && value.All(char.IsAsciiDigit)).ToArray();

    /// <summary>Reads only legacy lock identifiers for migration guidance; its display payload is never imported.</summary>
    public async Task<IReadOnlyList<string>> LegacyAppIdsAsync(CancellationToken cancellationToken = default)
    {
        var plugin = Path.Combine(_steam, "millennium", "plugins", "satli.star");
        if (!File.Exists(plugin)) return [];
        var snapshot = await ReadAsync(Path.Combine(_steam, "millennium", "config", "satli-bridge-v1.json"),
            TranslationJson.MaximumBytes, cancellationToken).ConfigureAwait(false);
        if (snapshot?["version"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != 1
            || snapshot["apps"] is not JsonObject apps) return [];
        return apps.Select(pair => pair.Key).Where(id => id.Length is > 0 and <= 10 && id[0] != '0'
            && id.All(char.IsAsciiDigit)).Order(StringComparer.Ordinal).ToArray();
    }

    public Task<LiteResult?> ResumeAsync(string appId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SerializedAsync(() => ResumeCoreAsync(appId, timeout, cancellationToken), cancellationToken);

    private async Task<LiteResult?> ResumeCoreAsync(string appId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var trackingPath = TrackingPath(appId);
        var tracked = await ReadAsync(trackingPath, MaximumRequestBytes * 4 + 65536, cancellationToken).ConfigureAwait(false);
        if (tracked is null && File.Exists(trackingPath))
            throw new PreflightException("暂时无法读取已有 lite 请求记录，请稍后刷新；不会创建重复请求。");
        if (tracked is null || !SteamMatches(tracked))
            return null;
        var request = TrackedRequest(tracked);
        var id = request["request_id"]?.ToString() ?? "";
        if (!Guid.TryParseExact(id, "D", out _) || id != id.ToLowerInvariant() || request["app_id"]?.ToString() != appId
            || request["client_id"]?.ToString() != ClientId)
            throw new IntegrityException("本机 lite 请求记录无效");
        // Recover a crash after local tracking was saved but before inbox publication.
        var locations = new[] { "inbox", "processing", "requests", "receipts", "results" };
        if (request["operation"]?.ToString() is "import" or "query_app"
            && !locations.Any(folder => File.Exists(Path.Combine(_root, folder, $"{id}.json")))
            && (await InspectAsync(cancellationToken).ConfigureAwait(false)).CanSubmit)
            await PublishAsync(request, TrackedBytes(tracked), cancellationToken).ConfigureAwait(false);
        var result = await WaitAsync(request, timeout, cancellationToken).ConfigureAwait(false);
        if (request["operation"]?.ToString() == "query_app" && tracked["last_import"] is JsonObject last)
        {
            var lastRequest = TrackedRequest(last);
            if (lastRequest["operation"]?.ToString() != "import" || lastRequest["app_id"]?.ToString() != appId)
                throw new IntegrityException("最近一次 lite 导入记录的操作或 App ID 不匹配");
            result = result with { LastImport = await WaitAsync(lastRequest, TimeSpan.Zero, cancellationToken).ConfigureAwait(false) };
        }
        return result;
    }

    public Task<LiteResult> QueryAsync(string appId, CancellationToken cancellationToken = default) => SerializedAsync(async () =>
    {
        var previous = await ResumeCoreAsync(appId, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        if (previous?.Pending == true) return previous;
        return await SubmitAsync(Request(appId, "query_app"), cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public Task<LiteResult> ImportAsync(
        string appId, JsonObject translation, string language, string sourceKind,
        JsonObject? authorizedConflict = null, CancellationToken cancellationToken = default, string? sourceVersion = null) => SerializedAsync(async () =>
    {
        translation = TranslationJson.Parse(JsonSerializer.SerializeToUtf8Bytes(translation), appId);
        var version = translation["version"]!.GetValue<int>();
        var connection = await InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!connection.CanSubmit) throw new PreflightException(connection.Message);
        if (!connection.TranslationVersions.Contains(version))
            throw new PreflightException($"当前 SATLI-lite 不支持格式 {version} JSON，请升级插件；已下载的 JSON 会保留。");
        var pending = await ResumeCoreAsync(appId, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        if (pending?.Pending == true)
            throw new PreflightException($"此游戏的请求 {pending.RequestId} 尚未完成，请刷新原请求状态，避免重复导入。");
        if (translation["app_id"]?.ToString() != appId
            || translation["languages"] is not JsonArray languages
            || !languages.Any(value => value?.ToString() == language))
            throw new IntegrityException("JSON 的 App ID 或目标语言不匹配");
        if (sourceKind is not ("catalog_json" or "converted" or "edited")
            || (sourceVersion is not null && Encoding.UTF8.GetByteCount(sourceVersion) > 128))
            throw new UsageException("lite 译文来源或来源版本无效");
        ValidateConflict(authorizedConflict);
        var request = Request(appId, "import");
        request["language"] = language;
        request["enabled"] = true;
        request["source"] = new JsonObject { ["kind"] = sourceKind };
        if (sourceVersion is not null) request["source"]!["version"] = sourceVersion;
        request["conflict"] = authorizedConflict?.DeepClone() ?? new JsonObject { ["mode"] = "reject" };
        request["translation"] = translation.DeepClone();
        return await SubmitAsync(request, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    private async Task<LiteResult> SubmitAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var connection = await InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!connection.CanSubmit) throw new PreflightException(connection.Message);
        var appId = request["app_id"]!.ToString();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request);
        if (bytes.Length > MaximumRequestBytes) throw new IntegrityException("lite 请求超过体积上限");
        // Save our own durable receipt before publishing. A timeout must retain the same ID.
        var tracked = new JsonObject { ["steam"] = _steam, ["request_bytes"] = Convert.ToBase64String(bytes) };
        var previous = await ReadAsync(TrackingPath(appId), MaximumRequestBytes * 4 + 65536, cancellationToken).ConfigureAwait(false);
        if (previous is not null && SteamMatches(previous))
        {
            var last = TrackedRequest(previous)["operation"]?.ToString() == "import" ? previous : previous["last_import"] as JsonObject;
            if (last is not null && request["operation"]?.ToString() != "import")
                tracked["last_import"] = new JsonObject { ["request_bytes"] = Convert.ToBase64String(TrackedBytes(last)) };
        }
        FileOperations.WriteDurable(TrackingPath(appId), JsonSerializer.SerializeToUtf8Bytes(tracked));
        await PublishAsync(request, bytes, cancellationToken).ConfigureAwait(false);
        return (await ResumeCoreAsync(appId, connection.Connected ? TimeSpan.FromSeconds(8) : TimeSpan.Zero, cancellationToken).ConfigureAwait(false))!;
    }

    private async Task PublishAsync(JsonObject request, byte[] bytes, CancellationToken cancellationToken)
    {
        var inbox = Path.Combine(_root, "inbox");
        EnsureSafePath(inbox);
        Directory.CreateDirectory(inbox);
        var id = request["request_id"]!.ToString();
        var temporary = Path.Combine(inbox, $"{id}.part");
        var destination = Path.Combine(inbox, $"{id}.json");
        EnsureSafePath(temporary);
        EnsureSafePath(destination);
        RecycleBin.FileIfExists(temporary);
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            65536, FileOptions.WriteThrough))
        {
            await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            output.Flush(true);
        }
        File.Move(temporary, destination); // Never overwrite an existing request.
    }

    private async Task<LiteResult> WaitAsync(JsonObject request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var id = request["request_id"]!.ToString();
        var operation = request["operation"]!.ToString();
        do
        {
            var receipt = await ReadAsync(Path.Combine(_root, "receipts", $"{id}.json"), 65536, cancellationToken).ConfigureAwait(false);
            if (receipt is not null && (!IsProtocolV1(receipt) || receipt["request_id"]?.ToString() != id
                || receipt["status"]?.ToString() is not ("accepted" or "failed")))
                throw new IntegrityException("lite 回执的协议、请求 ID 或状态无效");
            var result = receipt?["status"]?.ToString() == "failed" ? receipt
                : await ReadAsync(Path.Combine(_root, "results", $"{id}.json"), 65536, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                if (!IsProtocolV1(result) || result["request_id"]?.ToString() != id)
                    throw new IntegrityException("lite 响应的协议或请求 ID 不匹配");
                if (result["status"]?.ToString() == "failed" && IsBoolean(result["ok"], false))
                {
                    if (result["error"] is not JsonObject error || error["code"] is not JsonValue code
                        || !code.TryGetValue<string>(out var errorCode) || errorCode.Length is < 1 or > 64
                        || errorCode.Any(character => !char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character) && character != '_'))
                        throw new IntegrityException("lite 错误结果缺少错误码");
                    return new(id, operation, "failed", null, errorCode, Timestamp(result));
                }
                if (result["status"]?.ToString() != "succeeded" || !IsBoolean(result["ok"], true)
                    || result["client_id"]?.ToString() != ClientId || result["operation"]?.ToString() != operation
                    || result["app"] is not JsonObject app || app["app_id"]?.ToString() != request["app_id"]?.ToString())
                    throw new IntegrityException("lite 返回了不完整或不匹配的结果");
                ValidateApp(app);
                if (operation == "import" && !IsBoolean(app["exists"], true))
                    throw new IntegrityException("lite 导入成功结果没有已保存译本");
                return new(id, operation, "succeeded", (JsonObject)app.DeepClone(), ProcessedAt: Timestamp(result));
            }
            if (timer.Elapsed >= timeout)
            {
                var state = receipt?["status"]?.ToString() == "accepted" ? "accepted"
                    : new[] { "inbox", "processing", "requests" }.Any(folder => File.Exists(Path.Combine(_root, folder, $"{id}.json")))
                        ? "submitted" : "prepared";
                return new(id, operation, state, null);
            }
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        } while (true);
    }

    private static long Timestamp(JsonObject result) => result["processed_at"] is JsonValue value
        && value.TryGetValue<long>(out var timestamp) && timestamp is >= 0 and <= 253402300799
            ? timestamp : throw new IntegrityException("lite 处理结果缺少有效时间");

    private static void ValidateApp(JsonObject app)
    {
        if (IsBoolean(app["exists"], false)) return;
        if (!IsBoolean(app["exists"], true)) throw new IntegrityException("lite 游戏状态缺少 exists");
        foreach (var field in new[] { "saved", "enabled", "globally_enabled", "effective_enabled", "local_edits" })
            if (app[field] is not JsonValue value || !value.TryGetValue<bool>(out _))
                throw new IntegrityException($"lite 游戏状态缺少有效 {field}");
        foreach (var field in new[] { "variant_id", "language", "revision" })
            if (app[field] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrEmpty(text))
                throw new IntegrityException($"lite 游戏状态缺少有效 {field}");
        if (!IsBoolean(app["saved"], true)
            || IsBoolean(app["effective_enabled"], true) != (IsBoolean(app["enabled"], true) && IsBoolean(app["globally_enabled"], true)))
            throw new IntegrityException("lite 游戏保存或启用状态不一致");
    }

    private static void ValidateConflict(JsonObject? conflict)
    {
        if (conflict is null) return;
        if (conflict.Any(pair => pair.Key is not ("mode" or "expected_revision" or "allow_local_edits"))
            || conflict["mode"]?.ToString() is not ("reject" or "replace"))
            throw new UsageException("lite 覆盖策略无效");
        if (conflict["mode"]?.ToString() == "reject")
        {
            if (conflict.Count != 1) throw new UsageException("拒绝覆盖策略不能附带授权");
        }
        else if (conflict["expected_revision"] is not JsonValue revision
            || !revision.TryGetValue<string>(out var text) || Encoding.UTF8.GetByteCount(text) is < 1 or > 128
            || (conflict.ContainsKey("allow_local_edits") && conflict["allow_local_edits"] is not JsonValue))
            throw new UsageException("lite 覆盖需要查询结果中的有效 revision");
        if (conflict["allow_local_edits"] is JsonValue allow && !allow.TryGetValue<bool>(out _))
            throw new UsageException("lite 本地编辑覆盖授权必须为布尔值");
    }

    // Base64 keeps the exact wire bytes with a predictable tracking-file size, including very large JSON strings.
    private static byte[] TrackedBytes(JsonObject tracked)
    {
        if (tracked["request_bytes"] is JsonValue encoded && encoded.TryGetValue<string>(out var base64))
        {
            try { return Convert.FromBase64String(base64); }
            catch (FormatException exception) { throw new IntegrityException("本机 lite 请求字节记录无效", exception); }
        }
        return tracked["request"] is JsonObject legacy ? JsonSerializer.SerializeToUtf8Bytes(legacy)
            : throw new IntegrityException("本机 lite 请求记录缺少原始请求");
    }

    private bool SteamMatches(JsonObject tracked) => tracked["steam"] is JsonValue value
        && value.TryGetValue<string>(out var path)
        && string.Equals(Path.TrimEndingDirectorySeparator(path), _steam, StringComparison.OrdinalIgnoreCase);

    private static JsonObject TrackedRequest(JsonObject tracked)
    {
        var bytes = TrackedBytes(tracked);
        if (bytes.Length > MaximumRequestBytes) throw new IntegrityException("本机 lite 请求记录超过上限");
        var request = JsonNode.Parse(bytes) as JsonObject ?? throw new IntegrityException("本机 lite 请求记录无效");
        var id = request["request_id"]?.ToString() ?? "";
        if (!Guid.TryParseExact(id, "D", out _) || id != id.ToLowerInvariant()
            || request["client_id"]?.ToString() != ClientId || !IsProtocolV1(request)
            || request["operation"]?.ToString() is not ("import" or "query_app"))
            throw new IntegrityException("本机 lite 请求记录的 ID、客户端或操作无效");
        Request(request["app_id"]?.ToString() ?? "", "query_app");
        return request;
    }

    private async Task<T> SerializedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSafePath(_tracking);
            Directory.CreateDirectory(_tracking);
            // A persistent exclusive handle also serializes separate CLI processes. Never remove another owner's lock.
            var lockPath = Path.Combine(_tracking, "requests.lock");
            EnsureSafePath(lockPath);
            using var handle = OpenRequestLock(lockPath);
            return await action().ConfigureAwait(false);
        }
        finally { RequestGate.Release(); }
    }

    private static FileStream OpenRequestLock(string path)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new PreflightException("另一个 SATLI 请求操作正在使用记录；请稍后重试。", exception); }
    }

    private static JsonObject Request(string appId, string operation)
    {
        if (appId.Length is < 1 or > 10 || appId[0] == '0' || !appId.All(char.IsAsciiDigit))
            throw new UsageException("App ID 无效");
        return new JsonObject
        {
            ["protocol"] = 1, ["request_id"] = Guid.NewGuid().ToString(),
            ["client_id"] = ClientId, ["operation"] = operation, ["app_id"] = appId,
        };
    }

    private string TrackingPath(string appId)
    {
        Request(appId, "query_app");
        return Path.Combine(_tracking, $"{appId}.json");
    }

    private async Task<JsonObject?> ReadAsync(string path, int limit, CancellationToken cancellationToken)
    {
        EnsureSafePath(path);
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
            if (input.Length > limit) throw new IntegrityException("lite 接口文件超过安全上限");
            var bytes = new byte[(int)Math.Min(input.Length + 1, limit + 1L)];
            var count = await input.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            if (count > limit) throw new IntegrityException("lite 接口文件超过安全上限");
            return JsonNode.Parse(bytes.AsSpan(0, count)) as JsonObject;
        }
        catch (Exception exception) when (exception is IOException or JsonException) { return null; }
    }

    private static bool IsProtocolV1(JsonObject value) => value["protocol"] is JsonValue protocol
        && protocol.TryGetValue<int>(out var version) && version == 1;

    private static bool IsBoolean(JsonNode? value, bool expected) => value is JsonValue boolean
        && boolean.TryGetValue<bool>(out var actual) && actual == expected;

    private static void EnsureSafePath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new PreflightException("lite 外部接口路径不能包含链接或重解析点");
        }
    }

    private static bool IsSteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        try { return processes.Any(process => !process.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
