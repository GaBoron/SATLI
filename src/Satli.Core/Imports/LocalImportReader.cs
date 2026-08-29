using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Satli.Core.Formats;

namespace Satli.Core.Imports;

public sealed record LocalImportArtifact(string Source, string AppId, string SchemaName,
    byte[] Payload, string Sha256, JsonObject Preview);

public static partial class LocalImportReader
{
    private const long MaximumBytes = 64L * 1024 * 1024;
    public static LocalImportArtifact Read(string path)
    {
        var source = Path.GetFullPath(path); var match = NameRegex().Match(Path.GetFileName(source));
        if (!match.Success) throw new UsageException("本地导入文件必须命名为 UserGameStatsSchema_<app_id>.bin 或 UserGameStatsSchema_<app_id>.zip");
        if (!File.Exists(source)) throw new PreflightException($"未找到本地导入文件：{source}");
        if (new FileInfo(source).Length > MaximumBytes) throw new PreflightException("本地导入文件超过 64 MiB 限制");
        var appId = match.Groups[1].Value; var schemaName = $"UserGameStatsSchema_{appId}.bin";
        byte[] payload;
        if (match.Groups[2].Value.Equals("zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(source);
            if (archive.Entries.Count != 1 || archive.Entries[0].FullName != schemaName || archive.Entries[0].Length is <= 0 or > MaximumBytes)
                throw new PreflightException($"本地导入 ZIP 必须只包含根目录下的 {schemaName}");
            using var stream = archive.Entries[0].Open(); using var output = new MemoryStream(); stream.CopyTo(output); payload = output.ToArray();
        }
        else
        {
            if (new FileInfo(source).Length == 0) throw new PreflightException("本地导入 BIN 为空文件");
            payload = File.ReadAllBytes(source);
        }
        var preview = BinaryKeyValues.PreviewJson(payload);
        if (preview["achievement_count"]!.GetValue<int>() <= 0) throw new PreflightException("本地导入文件中没有可识别的 Steam 成就");
        return new LocalImportArtifact(source, appId, schemaName, payload,
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), preview);
    }
    [GeneratedRegex("^UserGameStatsSchema_([1-9][0-9]{0,19})\\.(bin|zip)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
