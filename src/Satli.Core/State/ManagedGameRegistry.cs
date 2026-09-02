using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.SchemaEditing;
using Satli.Core.SteamDisplay;
using Satli.Core.Transactions;

namespace Satli.Core.State;

public sealed record ManagedRecord(string AppId, string InstalledState, string? InstalledVariantId,
    string? InstalledSource, string? InstalledAt, string? InstalledSha256, string? GameName,
    bool DisplayOverrideEnabled);

public sealed class ManagedGameRegistry
{
    private readonly string _dataDirectory;
    public ManagedGameRegistry(string dataDirectory, string? steamDirectory = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        Installations = new TransactionManager(_dataDirectory);
        Edits = new EditHistoryStore(_dataDirectory);
        DisplayOverrides = string.IsNullOrWhiteSpace(steamDirectory)
            ? null
            : new SteamDisplayOverrideStore(
                SteamDisplayPluginInstaller.BridgePath(steamDirectory));
    }
    public TransactionManager Installations { get; }
    public EditHistoryStore Edits { get; }
    public SteamDisplayOverrideStore? DisplayOverrides { get; }

    public IReadOnlyList<string> ManagedAppIds() => Installations.Store.ManagedAppIds()
        .Concat(Edits.ManagedAppIds()).Distinct().OrderBy(value => ulong.Parse(value)).ToArray();

    public bool HasActiveTransaction(string appId) => Active(appId) is not null;

    public ManagedRecord Record(string appId)
    {
        var candidate = Active(appId); var active = candidate is not null;
        candidate ??= Latest(appId);
        if (candidate is null) return new(appId, "unmanaged", null, null, null, null, null, false);
        var transaction = candidate.Value.Transaction;
        if (candidate.Value.Source == "local-edit")
        {
            var hash = Text(transaction, "edited_sha256");
            return new(appId, active ? FileState(Text(transaction, "target"), hash) : "restored",
                hash is null ? "local-edit" : $"local-edit-{hash[..Math.Min(12, hash.Length)]}",
                "local-edit", Text(transaction, "edited_at"), hash, Text(transaction, "game_name"),
                DisplayOverrides?.IsEnabled(appId) is true);
        }
        var variant = Text(transaction, "variant_id"); var source = Text(transaction, "source_kind")
            ?? (variant?.StartsWith("local-", StringComparison.OrdinalIgnoreCase) == true ? "local-import" : "catalog");
        return new(appId, active ? Installations.Status(appId) : "restored", active ? variant : null,
            source, Text(transaction, "installed_at"), Text(transaction, "installed_sha256"),
            Text(transaction, "game_name"), DisplayOverrides?.IsEnabled(appId) is true);
    }

    public string? RestorePreviewSource(string appId, string expectedTarget)
    {
        var candidate = Active(appId) ?? throw new TransactionException($"{appId} 没有可恢复的管理记录");
        if (candidate.Source == "installation") return Installations.RestorePreviewSource(appId, expectedTarget);
        var target = Path.GetFullPath(Text(candidate.Transaction, "target") ?? "");
        if (!target.Equals(Path.GetFullPath(expectedTarget), StringComparison.OrdinalIgnoreCase)) throw new TransactionException("编辑历史中的目标路径与当前 Steam 目录不一致");
        var snapshot = SafeRelative(Text(candidate.Transaction, "snapshot") ?? "");
        var expected = Text(candidate.Transaction, "original_sha256") ?? "";
        if (!File.Exists(snapshot) || FileOperations.Sha256(snapshot) != expected) throw new IntegrityException($"编辑前备份 SHA-256 不匹配：{snapshot}");
        return snapshot;
    }

    public JsonObject Restore(string appId, string target, bool force)
    {
        var candidate = Active(appId) ?? throw new TransactionException($"{appId} 没有可恢复的管理记录");
        return candidate.Source == "local-edit"
            ? new SchemaEditor().Restore(target, appId, _dataDirectory, force)
            : Installations.Restore(appId, target, force);
    }

    private (string Source, JsonObject Transaction)? Active(string appId) => Choose(
        Installations.Store.ActiveTransaction(appId), Edits.Active(appId));
    private (string Source, JsonObject Transaction)? Latest(string appId) => Choose(
        Installations.Store.Transactions(appId).LastOrDefault(), Edits.Transactions(appId).LastOrDefault());
    private static (string Source, JsonObject Transaction)? Choose(JsonObject? installation, JsonObject? edit)
    {
        if (installation is null) return edit is null ? null : ("local-edit", edit);
        if (edit is null) return ("installation", installation);
        var installedAt = Timestamp(Text(installation, "installed_at")); var editedAt = Timestamp(Text(edit, "edited_at"));
        if (installedAt != editedAt) return installedAt > editedAt ? ("installation", installation) : ("local-edit", edit);
        if (Text(installation, "previous_sha256") == Text(edit, "edited_sha256")) return ("installation", installation);
        return ("local-edit", edit);
    }
    private string SafeRelative(string value) { var full = Path.GetFullPath(Path.Combine(_dataDirectory, value)); if (Path.GetRelativePath(_dataDirectory, full).StartsWith("..", StringComparison.Ordinal)) throw new TransactionException("备份路径越界"); return full; }
    private static string FileState(string? target, string? expected) { if (string.IsNullOrWhiteSpace(target) || !File.Exists(target)) return "missing"; try { return FileOperations.Sha256(target) == expected ? "installed" : "modified"; } catch { return "unreadable"; } }
    private static string? Text(JsonObject value, string name) => StateStore.String(value, name);
    private static DateTimeOffset Timestamp(string? value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
}
