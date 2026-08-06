using System.IO.Compression;

namespace Satl_Gui.Services;

internal static class SchemaZipArchive
{
    public static byte[] ReadSingleSchema(string zipPath, string appId)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var expectedMember = $"UserGameStatsSchema_{appId}.bin";
        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (files.Length != 1
            || !files[0].FullName.Equals(expectedMember, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"ZIP 必须只包含根目录下的 {expectedMember}。");
        }
        using var stream = files[0].Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
