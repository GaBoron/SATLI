using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Satli.Core.FileSystem;

namespace Satli.Core.SatliLite;

/// <summary>Uses only SATLI-lite's public v1 mailbox; never reads its private translation state.</summary>
public sealed class LiteClient
{
    private readonly string _steam;
    private readonly string _root;
    private readonly string _tracking;
    private readonly Func<bool> _steamRunning;
    private const int MaximumRequestBytes = 32 * 1024 * 1024 + 65536;
    public const string ClientId = "satli-desktop";

    public LiteClient(string steamDirectory, string dataDirectory, Func<bool>? steamRunning = null)
    {
        _steam = Path.GetFullPath(steamDirectory);
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
        if (!File.Exists(PluginPath))
            return new("not_installed", "未安装 SATLI-lite。", []);
        if (File.Exists(Path.Combine(_steam, "millennium", "plugins", "satli.star")))
            return new("legacy_bridge", "发现旧 SATLI 显示桥，请先退出 Steam 并移除旧桥，避免双桥冲突。", []);
        if (!_steamRunning())
            return new("steam_stopped", "Steam 未运行，请启动 Steam 并启用 SATLI-lite 后重试。", []);
        var protocol = await ReadAsync(Path.Combine(_root, "protocol.json"), 65536, cancellationToken).ConfigureAwait(false);
        if (protocol is null)
            return new("unavailable", "尚未连接：请在 Steam → Millennium → Plugins 启用 SATLI-lite，重启 Steam 后重试。", []);
        if (!IsProtocolV1(protocol)
            || protocol["plugin_id"]?.ToString() != "com.gaboron.satli-lite"
            || protocol["operations"] is not JsonArray operations
            || !operations.Any(value => value?.ToString() == "import")
            || !operations.Any(value => value?.ToString() == "query_app"))
            return new("incompatible", "SATLI-lite 外部协议或能力不兼容，请升级插件。", []);
        var versions = protocol["translation_versions"] is JsonArray formats
            ? formats.Select(value => value is JsonValue number && number.TryGetValue<int>(out var version) ? version : 0).ToArray()
            : new[] { 1 };
        var runtime = await ReadAsync(Path.Combine(_root, "runtime.json"), 65536, cancellationToken).ConfigureAwait(false);
        var heartbeat = long.TryParse(runtime?["heartbeat_at"]?.ToString(), out var time) ? time : 0;
        var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - heartbeat;
        if (runtime is null || !IsProtocolV1(runtime) || age is < 0 or > 15
            || !IsBoolean(runtime["backend_running"], true)
            || !IsBoolean(runtime["accepting_requests"], true))
            return new("unavailable", "Steam 正在运行，但 lite 没有有效心跳；请检查插件启用状态或重启 Steam，再刷新连接。", versions);
        return new("connected", "已连接 SATLI-lite。", versions);
    }

    public IReadOnlyList<string> KnownAppIds() => !Directory.Exists(_tracking) ? []
        : Directory.EnumerateFiles(_tracking, "*.json")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(value => value.Length is > 0 and <= 10 && value.All(char.IsAsciiDigit)).ToArray();

    public async Task<LiteResult?> ResumeAsync(string appId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var trackingPath = TrackingPath(appId);
        var tracked = await ReadAsync(trackingPath, MaximumRequestBytes + 65536, cancellationToken).ConfigureAwait(false);
        if (tracked is null && File.Exists(trackingPath))
            throw new PreflightException("暂时无法读取已有 lite 请求记录，请稍后刷新；不会创建重复请求。");
        if (tracked is null || tracked["steam"]?.ToString() != _steam || tracked["request"] is not JsonObject request)
            return null;
        var id = request["request_id"]?.ToString() ?? "";
        if (!Guid.TryParseExact(id, "D", out _) || id != id.ToLowerInvariant() || request["app_id"]?.ToString() != appId
            || request["client_id"]?.ToString() != ClientId)
            throw new IntegrityException("本机 lite 请求记录无效");
        // Recover a crash after local tracking was saved but before inbox publication.
        var locations = new[] { "inbox", "processing", "requests", "receipts", "results" };
        if (request["operation"]?.ToString() is "import" or "query_app"
            && !locations.Any(folder => File.Exists(Path.Combine(_root, folder, $"{id}.json")))
            && (await InspectAsync(cancellationToken).ConfigureAwait(false)).Connected)
            await PublishAsync(request, cancellationToken).ConfigureAwait(false);
        return await WaitAsync(request, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LiteResult> QueryAsync(string appId, CancellationToken cancellationToken = default)
    {
        var previous = await ResumeAsync(appId, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        if (previous?.Pending == true) return previous;
        return await SubmitAsync(Request(appId, "query_app"), cancellationToken).ConfigureAwait(false);
    }

    public async Task<LiteResult> ImportAsync(
        string appId, JsonObject translation, string language, string sourceKind,
        JsonObject? authorizedConflict = null, CancellationToken cancellationToken = default)
    {
        var version = translation["version"]!.GetValue<int>();
        var connection = await InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!connection.Connected) throw new PreflightException(connection.Message);
        if (!connection.TranslationVersions.Contains(version))
            throw new PreflightException($"当前 SATLI-lite 不支持格式 {version} JSON，请升级插件；已下载的 JSON 会保留。");
        var pending = await ResumeAsync(appId, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        if (pending?.Pending == true)
            throw new PreflightException($"此游戏的请求 {pending.RequestId} 尚未完成，请刷新原请求状态，避免重复导入。");
        if (translation["app_id"]?.ToString() != appId
            || translation["languages"] is not JsonArray languages
            || !languages.Any(value => value?.ToString() == language))
            throw new IntegrityException("JSON 的 App ID 或目标语言不匹配");
        var request = Request(appId, "import");
        request["language"] = language;
        request["enabled"] = true;
        request["source"] = new JsonObject { ["kind"] = sourceKind, ["version"] = "2.4.1" };
        request["conflict"] = authorizedConflict?.DeepClone() ?? new JsonObject { ["mode"] = "reject" };
        request["translation"] = translation.DeepClone();
        return await SubmitAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LiteResult> SubmitAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var connection = await InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!connection.Connected) throw new PreflightException(connection.Message);
        var appId = request["app_id"]!.ToString();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request);
        if (bytes.Length > MaximumRequestBytes) throw new IntegrityException("lite 请求超过体积上限");
        // Save our own durable receipt before publishing. A timeout must retain the same ID.
        var tracked = new JsonObject { ["steam"] = _steam, ["request"] = request.DeepClone() };
        FileOperations.WriteDurable(TrackingPath(appId), JsonSerializer.SerializeToUtf8Bytes(tracked));
        await PublishAsync(request, cancellationToken).ConfigureAwait(false);
        return await WaitAsync(request, TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var inbox = Path.Combine(_root, "inbox");
        EnsureSafePath(inbox);
        Directory.CreateDirectory(inbox);
        var id = request["request_id"]!.ToString();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request);
        var temporary = Path.Combine(inbox, $"{id}.part");
        var destination = Path.Combine(inbox, $"{id}.json");
        EnsureSafePath(temporary);
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
            var result = receipt?["status"]?.ToString() == "failed" ? receipt
                : await ReadAsync(Path.Combine(_root, "results", $"{id}.json"), 65536, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                if (!IsProtocolV1(result) || result["request_id"]?.ToString() != id)
                    throw new IntegrityException("lite 响应的协议或请求 ID 不匹配");
                if (result["status"]?.ToString() == "failed" && IsBoolean(result["ok"], false))
                    return new(id, operation, "failed", null, result["error"]?["code"]?.ToString() ?? "UNKNOWN");
                if (result["status"]?.ToString() != "succeeded" || !IsBoolean(result["ok"], true)
                    || result["client_id"]?.ToString() != ClientId || result["operation"]?.ToString() != operation
                    || result["app"] is not JsonObject app || app["app_id"]?.ToString() != request["app_id"]?.ToString())
                    throw new IntegrityException("lite 返回了不完整或不匹配的结果");
                return new(id, operation, "succeeded", (JsonObject)app.DeepClone());
            }
            if (timer.Elapsed >= timeout) break;
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        } while (true);
        return new(id, operation, "pending", null);
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
