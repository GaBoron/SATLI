using Satli.Core.FileSystem;

namespace Satli.Core.State;

public static class DataDirectoryMigration
{
    public static string MigrateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(local, "SATLI");
        var legacy = Path.Combine(local, "SteamAchievementTranslationInstaller");
        if (!Directory.Exists(legacy)) return current;
        if (Directory.Exists(current))
        {
            if (Directory.EnumerateFileSystemEntries(current).Any())
                throw new TransactionException($"无法迁移旧数据目录：目标目录已包含文件：{current}");
            RecycleBin.DirectoryIfExists(current);
        }
        Directory.Move(legacy, current);
        var updates = Path.Combine(current, "updates");
        if (Directory.Exists(updates))
        {
            foreach (var pattern in new[]
            {
                "SATLInstaller-Setup-v*.exe",
                "SATLInstaller-Setup-v*.exe.part",
            })
            {
                foreach (var path in Directory.EnumerateFiles(updates, pattern))
                    RecycleBin.FileIfExists(path);
            }
        }
        return current;
    }
}
