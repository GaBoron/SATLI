using System.Security.Cryptography;

namespace Satli.Core.FileSystem;

public static class FileOperations
{
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Sha256(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    public static bool IsReadOnly(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PreflightException($"无法读取文件保护状态：{path}：{exception.Message}", exception);
        }
    }

    public static void SetReadOnly(string path, bool enabled)
    {
        if (!File.Exists(path))
        {
            throw new PreflightException($"找不到要保护的 Steam 成就文件：{path}");
        }
        try
        {
            var attributes = File.GetAttributes(path);
            File.SetAttributes(
                path,
                enabled
                    ? attributes | FileAttributes.ReadOnly
                    : attributes & ~FileAttributes.ReadOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var action = enabled ? "设置" : "清除";
            throw new TransactionException($"无法{action}只读属性：{path}：{exception.Message}", exception);
        }
        if (IsReadOnly(path) != enabled)
        {
            throw new TransactionException(
                $"Windows 未将文件切换为预期的{(enabled ? "只读" : "可写")}状态：{path}");
        }
    }

    public static void RecycleFile(string path)
    {
        if (File.Exists(path) && IsReadOnly(path))
        {
            SetReadOnly(path, false);
        }
        RecycleBin.FileIfExists(path);
    }

    public static void ReplaceStaged(string stage, string target)
    {
        var targetExisted = File.Exists(target);
        var targetReadOnly = targetExisted && IsReadOnly(target);
        if (targetReadOnly)
        {
            SetReadOnly(target, false);
            var attributes = File.GetAttributes(stage) | FileAttributes.ReadOnly;
            File.SetAttributes(stage, attributes);
        }
        try
        {
            if (targetExisted)
            {
                var replacedBackup = Path.Combine(
                    Path.GetDirectoryName(target)!,
                    $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.replaced");
                try
                {
                    File.Replace(stage, target, replacedBackup, true);
                }
                finally
                {
                    RecycleBin.FileIfExists(replacedBackup);
                }
            }
            else
            {
                File.Move(stage, target);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (targetReadOnly && File.Exists(target))
            {
                try
                {
                    SetReadOnly(target, true);
                }
                catch
                {
                }
            }
            throw new TransactionException(
                $"替换目标文件失败（阶段=replace）：{stage} -> {target}：{exception.Message}",
                exception);
        }
    }

    public static void CopyDurable(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan))
            using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            ReplaceStaged(temporary, destination);
        }
        catch (SatliException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new TransactionException(
                $"无法复制文件：{source} -> {destination}：{exception.Message}",
                exception);
        }
        finally
        {
            RecycleBin.FileIfExists(temporary);
        }
    }

    public static void WriteDurable(string destination, ReadOnlySpan<byte> payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.WriteThrough))
            {
                output.Write(payload);
                output.Flush(true);
            }
            ReplaceStaged(temporary, destination);
        }
        catch (SatliException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new TransactionException($"无法写入文件：{destination}：{exception.Message}", exception);
        }
        finally
        {
            RecycleBin.FileIfExists(temporary);
        }
    }
}
