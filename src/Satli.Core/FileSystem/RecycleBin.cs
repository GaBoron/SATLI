using Microsoft.VisualBasic.FileIO;

namespace Satli.Core.FileSystem;

public static class RecycleBin
{
    public static void FileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            Path.GetFullPath(path),
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
    }

    public static void DirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
            Path.GetFullPath(path),
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
    }
}
