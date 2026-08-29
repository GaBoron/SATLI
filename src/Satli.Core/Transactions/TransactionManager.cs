using System.Text.Json.Nodes;
using Satli.Core.Catalog;
using Satli.Core.FileSystem;
using Satli.Core.Models;
using Satli.Core.State;

namespace Satli.Core.Transactions;

public sealed class TransactionManager
{
    public TransactionManager(string dataDirectory, StateStore? store = null)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        Store = store ?? new StateStore(DataDirectory);
    }

    public string DataDirectory { get; }
    public StateStore Store { get; }

    public JsonObject Install(
        string appId,
        string target,
        string source,
        SchemaVariant variant,
        string sourceKind = "catalog",
        string? gameName = null,
        bool dryRun = false)
    {
        if (sourceKind is not ("catalog" or "local-import"))
        {
            throw new TransactionException($"不支持的安装来源：{sourceKind}");
        }
        CatalogRepository.VerifySchemaFile(source, variant);
        target = Path.GetFullPath(target);
        if (Path.GetFileName(target) != $"UserGameStatsSchema_{appId}.bin")
        {
            throw new TransactionException($"目标文件名与 App ID 不匹配：{target}");
        }
        if (dryRun)
        {
            return new JsonObject { ["app_id"] = appId, ["action"] = "would-install", ["target"] = target };
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var backupDirectory = Path.Combine(DataDirectory, "backups", appId, transactionId);
        var snapshot = Path.Combine(backupDirectory, "original.bin");
        var stage = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{transactionId}.tmp");
        var previousExists = File.Exists(target);
        string? previousSha256 = null;
        var replaced = false;
        try
        {
            if (previousExists)
            {
                previousSha256 = FileOperations.Sha256(target);
                FileOperations.CopyDurable(target, snapshot);
                if (FileOperations.Sha256(snapshot) != previousSha256)
                {
                    throw new IntegrityException($"安装前备份校验失败：{snapshot}");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            FileOperations.CopyDurable(source, stage);
            CatalogRepository.VerifySchemaFile(stage, variant);
            FileOperations.ReplaceStaged(stage, target);
            replaced = true;
            var transaction = new JsonObject
            {
                ["id"] = transactionId,
                ["installed_at"] = UtcNow(),
                ["source_kind"] = sourceKind,
                ["game_name"] = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim(),
                ["variant_id"] = variant.VariantId,
                ["schema_file"] = variant.SchemaFile,
                ["source_sha256"] = variant.Sha256,
                ["installed_sha256"] = variant.Sha256,
                ["target"] = target,
                ["previous_exists"] = previousExists,
                ["previous_sha256"] = previousSha256,
                ["snapshot"] = previousExists ? Relative(snapshot) : null,
            };
            try
            {
                Store.AddTransaction(appId, transaction);
            }
            catch (TransactionException exception)
            {
                RollbackInstall(target, snapshot, previousExists, variant.Sha256);
                throw new TransactionException($"写入状态失败，已回滚目标文件：{exception.Message}", exception);
            }
            return transaction;
        }
        catch (SatliException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (replaced)
            {
                RollbackInstall(target, snapshot, previousExists, variant.Sha256);
            }
            throw new TransactionException($"安装 {appId} 失败：{exception.Message}", exception);
        }
        finally
        {
            RecycleBin.FileIfExists(stage);
            if (!replaced)
            {
                RecycleBin.DirectoryIfExists(backupDirectory);
            }
        }
    }

    public JsonObject Restore(
        string appId,
        string expectedTarget,
        bool force = false,
        bool dryRun = false)
    {
        var transaction = Store.ActiveTransaction(appId)
            ?? throw new TransactionException($"{appId} 没有可恢复的安装记录");
        var target = Path.GetFullPath(StateStore.String(transaction, "target") ?? string.Empty);
        if (!target.Equals(Path.GetFullPath(expectedTarget), StringComparison.OrdinalIgnoreCase))
        {
            throw new TransactionException($"状态文件中的目标路径与当前 Steam 目录不一致：{target}");
        }
        var expectedHash = StateStore.String(transaction, "installed_sha256") ?? string.Empty;
        var targetExists = File.Exists(target);
        var currentHash = targetExists ? FileOperations.Sha256(target) : null;
        var unchanged = targetExists && currentHash == expectedHash;
        if (!unchanged && !force)
        {
            var state = targetExists ? $"已修改（当前 {currentHash}）" : "缺失";
            throw new TransactionException($"拒绝恢复 {appId}：目标文件{state}；如确认继续请使用 --force");
        }
        if (dryRun)
        {
            return new JsonObject { ["app_id"] = appId, ["action"] = "would-restore", ["target"] = target };
        }
        var transactionId = StateStore.String(transaction, "id")
            ?? throw new TransactionException($"{appId} 的事务 ID 无效");
        var backupDirectory = Path.Combine(DataDirectory, "backups", appId, transactionId);
        var restoreId = Guid.NewGuid().ToString("N");
        var rollback = Path.Combine(backupDirectory, $"restore-rollback-{restoreId}.bin");
        var forcedArchive = Path.Combine(backupDirectory, $"forced-current-{restoreId}.bin");
        var cleanupRollback = true;
        try
        {
            if (targetExists)
            {
                FileOperations.CopyDurable(target, rollback);
                if (force && !unchanged)
                {
                    FileOperations.CopyDurable(target, forcedArchive);
                }
            }
            if (StateStore.Boolean(transaction, "previous_exists"))
            {
                var snapshot = ResolveRelative(StateStore.String(transaction, "snapshot")
                    ?? throw new TransactionException($"{appId} 的备份路径缺失"));
                if (!File.Exists(snapshot))
                {
                    throw new TransactionException($"找不到安装前备份：{snapshot}");
                }
                var previousHash = StateStore.String(transaction, "previous_sha256") ?? string.Empty;
                if (FileOperations.Sha256(snapshot) != previousHash)
                {
                    throw new IntegrityException($"安装前备份 SHA-256 不匹配：{snapshot}");
                }
                FileOperations.CopyDurable(snapshot, target);
            }
            else
            {
                FileOperations.RecycleFile(target);
            }
            var forcedValue = File.Exists(forcedArchive) ? Relative(forcedArchive) : null;
            try
            {
                Store.MarkRestored(appId, transactionId, UtcNow(), forcedValue);
            }
            catch (TransactionException exception)
            {
                try
                {
                    RollbackRestore(target, rollback, targetExists);
                }
                catch (TransactionException rollbackException)
                {
                    cleanupRollback = false;
                    throw new TransactionException(
                        $"保存恢复状态失败且文件回滚失败：{exception.Message}；"
                        + $"恢复副本保留在 {rollback}：{rollbackException.Message}",
                        exception);
                }
                throw new TransactionException($"保存恢复状态失败，已回滚：{exception.Message}", exception);
            }
            return new JsonObject
            {
                ["app_id"] = appId,
                ["action"] = "restored",
                ["target"] = target,
                ["forced_archive"] = forcedValue,
            };
        }
        finally
        {
            if (cleanupRollback)
            {
                RecycleBin.FileIfExists(rollback);
            }
        }
    }

    public string? RestorePreviewSource(string appId, string expectedTarget)
    {
        var transaction = Store.ActiveTransaction(appId)
            ?? throw new TransactionException($"{appId} 没有可恢复的安装记录");
        var target = Path.GetFullPath(StateStore.String(transaction, "target") ?? string.Empty);
        if (!target.Equals(Path.GetFullPath(expectedTarget), StringComparison.OrdinalIgnoreCase))
        {
            throw new TransactionException($"状态文件中的目标路径与当前 Steam 目录不一致：{target}");
        }
        if (!StateStore.Boolean(transaction, "previous_exists"))
        {
            return null;
        }
        var snapshot = ResolveRelative(StateStore.String(transaction, "snapshot")
            ?? throw new TransactionException($"{appId} 的备份路径缺失"));
        if (!File.Exists(snapshot))
        {
            throw new TransactionException($"找不到安装前备份：{snapshot}");
        }
        if (FileOperations.Sha256(snapshot)
            != (StateStore.String(transaction, "previous_sha256") ?? string.Empty))
        {
            throw new IntegrityException($"安装前备份 SHA-256 不匹配：{snapshot}");
        }
        return snapshot;
    }

    public string Status(string appId)
    {
        var transaction = Store.ActiveTransaction(appId);
        if (transaction is null)
        {
            return Store.Transactions(appId).Count > 0 ? "restored" : "unmanaged";
        }
        var target = StateStore.String(transaction, "target") ?? string.Empty;
        if (!File.Exists(target))
        {
            return "missing";
        }
        try
        {
            return FileOperations.Sha256(target)
                == StateStore.String(transaction, "installed_sha256")
                    ? "installed"
                    : "modified";
        }
        catch
        {
            return "unreadable";
        }
    }

    public static string UtcNow() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private string Relative(string path)
    {
        var relative = Path.GetRelativePath(DataDirectory, Path.GetFullPath(path));
        if (relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new TransactionException($"备份路径越界：{path}");
        }
        return relative.Replace('\\', '/');
    }

    private string ResolveRelative(string value)
    {
        var candidate = Path.GetFullPath(Path.Combine(DataDirectory, value));
        var relative = Path.GetRelativePath(DataDirectory, candidate);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new TransactionException($"状态文件中的备份路径越界：{value}");
        }
        return candidate;
    }

    private static void RollbackInstall(
        string target,
        string snapshot,
        bool previousExists,
        string installedHash)
    {
        if (previousExists)
        {
            if (!File.Exists(snapshot))
            {
                throw new TransactionException($"回滚备份不存在：{snapshot}");
            }
            FileOperations.CopyDurable(snapshot, target);
        }
        else if (File.Exists(target))
        {
            if (FileOperations.Sha256(target) != installedHash)
            {
                throw new TransactionException("目标文件已变化，不能安全移除");
            }
            FileOperations.RecycleFile(target);
        }
    }

    private static void RollbackRestore(string target, string rollback, bool beforeExists)
    {
        if (beforeExists)
        {
            if (!File.Exists(rollback))
            {
                throw new TransactionException($"恢复回滚文件不存在：{rollback}");
            }
            FileOperations.CopyDurable(rollback, target);
        }
        else
        {
            FileOperations.RecycleFile(target);
        }
    }
}
