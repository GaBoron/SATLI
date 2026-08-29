using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Satli.Core.FileSystem;
using Satli.Core.Formats;

namespace Satli.Core.SchemaEditing;

public sealed record RenderedSchema(byte[] Payload, JsonObject Report);

public sealed partial class SchemaEditor
{
    public JsonObject Inspect(string path, string appId, string dataDirectory)
    {
        path = ValidatePath(path, appId);
        var payload = File.ReadAllBytes(path);
        var result = BinaryKeyValues.PreviewJson(payload);
        result["app_id"] = appId;
        result["source_path"] = path;
        result["source_sha256"] = Sha256(payload);
        result["can_restore"] = new EditHistoryStore(dataDirectory).Active(appId) is not null;
        return result;
    }

    public RenderedSchema Render(string sourcePath, string appId, string targetLanguage,
        string editsPath, bool allowIncomplete)
    {
        sourcePath = ValidatePath(sourcePath, appId);
        var language = ValidateLanguage(targetLanguage);
        var original = File.ReadAllBytes(sourcePath);
        var originalHash = Sha256(original);
        var edits = LoadEdits(editsPath, appId, language, originalHash);
        var nodes = BinaryKeyValues.Parse(original);
        if (!BinaryKeyValues.Serialize(nodes).AsSpan().SequenceEqual(original))
            throw new IntegrityException("原始 Binary KeyValues 文件未通过字节级 roundtrip 校验");

        var achievements = AchievementNodes(nodes).ToArray();
        var byId = achievements.ToDictionary(item => item.ApiName, StringComparer.Ordinal);
        if (byId.Count != achievements.Length)
            throw new PreflightException("原始 schema 包含重复的成就 API ID，拒绝编辑");
        var missingIds = byId.Keys.Except(edits.Keys, StringComparer.Ordinal).Order().ToArray();
        var extraIds = edits.Keys.Except(byId.Keys, StringComparer.Ordinal).Order().ToArray();
        if (missingIds.Length > 0 || extraIds.Length > 0)
            throw new UsageException("编辑内容的成就 ID 集合与源文件不一致"
                + (missingIds.Length > 0 ? $"；缺少：{string.Join(", ", missingIds)}" : "")
                + (extraIds.Length > 0 ? $"；多余：{string.Join(", ", extraIds)}" : ""));

        var missingNames = 0; var missingDescriptions = 0;
        var changedNames = 0; var changedDescriptions = 0;
        foreach (var (apiName, nameNode, descriptionNode) in achievements)
        {
            var edit = edits[apiName];
            ValidateText(edit.Name, apiName, "名称");
            ValidateText(edit.Description, apiName, "说明");
            if (edit.Name.Length == 0) missingNames++;
            if (edit.Description.Length == 0) missingDescriptions++;
            changedNames += SetLanguageValue(nameNode, language, edit.Name);
            changedDescriptions += SetLanguageValue(descriptionNode, language, edit.Description);
        }
        if (!allowIncomplete && (missingNames > 0 || missingDescriptions > 0))
            throw new PreflightException($"目标语言内容不完整：缺少名称 {missingNames} 项，缺少说明 {missingDescriptions} 项；确认风险后使用 --allow-incomplete");
        var localized = BinaryKeyValues.Serialize(nodes);
        var preview = BinaryKeyValues.Preview(localized);
        if (preview.AchievementCount != achievements.Length)
            throw new IntegrityException("编辑后成就数量发生变化，拒绝输出");
        var completeLanguages = preview.Languages.Where(languageCode => preview.Rows.Count > 0
            && preview.Rows.All(row => row.Translations.TryGetValue(languageCode, out var value)
                && value.Name.Length > 0 && value.Description.Length > 0)).ToArray();
        var report = new JsonObject
        {
            ["app_id"] = appId, ["target_language"] = language,
            ["source_sha256"] = originalHash, ["output_sha256"] = Sha256(localized),
            ["achievement_count"] = achievements.Length,
            ["changed_fields"] = changedNames + changedDescriptions,
            ["changed_names"] = changedNames, ["changed_descriptions"] = changedDescriptions,
            ["missing_names"] = missingNames, ["missing_descriptions"] = missingDescriptions,
            ["incomplete"] = missingNames > 0 || missingDescriptions > 0,
            ["roundtrip_equal"] = true,
            ["complete_languages"] = Strings(completeLanguages),
            ["submission_languages"] = Strings(preview.Languages),
        };
        return new RenderedSchema(localized, report);
    }

    public JsonObject Export(RenderedSchema rendered, string sourcePath, string appId,
        string outputPath, string format)
    {
        outputPath = Path.GetFullPath(outputPath);
        if (outputPath.Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            throw new UsageException("导出路径不能覆盖 Steam 当前使用的原始文件");
        if (format == "bin" && Path.GetExtension(outputPath).Equals(".bin", StringComparison.OrdinalIgnoreCase))
            FileOperations.WriteDurable(outputPath, rendered.Payload);
        else if (format == "zip" && Path.GetExtension(outputPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            WriteZip(outputPath, $"UserGameStatsSchema_{appId}.bin", rendered.Payload);
        else throw new UsageException(format == "bin" ? "BIN 导出路径必须使用 .bin 扩展名" : "ZIP 导出路径必须使用 .zip 扩展名");
        rendered.Report["output"] = outputPath;
        rendered.Report["format"] = format;
        return rendered.Report;
    }

    public JsonObject Apply(string sourcePath, string appId, byte[] payload, string dataDirectory,
        string? gameName, string? targetLanguage, JsonObject? report = null)
    {
        sourcePath = ValidatePath(sourcePath, appId);
        var current = File.ReadAllBytes(sourcePath);
        var currentHash = Sha256(current); var outputHash = Sha256(payload);
        if (currentHash == outputHash) throw new PreflightException("目标版本与当前文件完全相同，无需写回");
        var preview = BinaryKeyValues.Preview(payload);
        report ??= new JsonObject();
        report["app_id"] = appId; report["target_language"] = targetLanguage?.Trim().ToLowerInvariant() ?? "";
        report["source_sha256"] = currentHash; report["output_sha256"] = outputHash;
        report["achievement_count"] = preview.AchievementCount;
        foreach (var key in new[] { "changed_fields", "changed_names", "changed_descriptions", "missing_names", "missing_descriptions" })
            report[key] ??= 0;
        report["incomplete"] ??= false; report["roundtrip_equal"] = true;
        report["complete_languages"] ??= Strings(preview.Languages.Where(code => preview.Rows.All(row => row.Translations.TryGetValue(code, out var value) && value.Name.Length > 0 && value.Description.Length > 0)));
        report["submission_languages"] ??= Strings(preview.Languages);

        var store = new EditHistoryStore(dataDirectory); var id = Guid.NewGuid().ToString("N");
        var backup = Path.Combine(store.DataDirectory, "edit-backups", appId, id, "original.bin");
        var stage = Path.Combine(Path.GetDirectoryName(sourcePath)!, $".{Path.GetFileName(sourcePath)}.{id}.tmp");
        var replaced = false;
        try
        {
            FileOperations.CopyDurable(sourcePath, backup);
            if (FileOperations.Sha256(backup) != currentHash) throw new IntegrityException($"编辑前备份校验失败：{backup}");
            FileOperations.WriteDurable(stage, payload);
            if (FileOperations.Sha256(stage) != outputHash) throw new IntegrityException("编辑暂存文件 SHA-256 校验失败");
            BinaryKeyValues.Preview(File.ReadAllBytes(stage));
            FileOperations.ReplaceStaged(stage, sourcePath); replaced = true;
            var transaction = new JsonObject
            {
                ["id"] = id, ["edited_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["game_name"] = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim(),
                ["target"] = sourcePath, ["target_language"] = targetLanguage ?? "",
                ["original_sha256"] = currentHash, ["edited_sha256"] = outputHash,
                ["snapshot"] = Path.GetRelativePath(store.DataDirectory, backup).Replace('\\', '/'),
            };
            try { store.Add(appId, transaction); }
            catch (TransactionException exception)
            {
                FileOperations.CopyDurable(backup, sourcePath);
                throw new TransactionException($"保存编辑历史失败，已回滚本地文件：{exception.Message}", exception);
            }
            report["target"] = sourcePath; report["backup"] = backup;
            return report;
        }
        finally
        {
            RecycleBin.FileIfExists(stage);
            if (!replaced) RecycleBin.DirectoryIfExists(Path.GetDirectoryName(backup)!);
        }
    }

    public JsonObject Restore(string sourcePath, string appId, string dataDirectory, bool force)
    {
        sourcePath = ValidatePath(sourcePath, appId);
        var store = new EditHistoryStore(dataDirectory);
        var transaction = store.Active(appId) ?? throw new TransactionException($"{appId} 没有可恢复的本地编辑记录");
        if (!Path.GetFullPath(transaction["target"]!.GetValue<string>()).Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
            throw new TransactionException("编辑历史中的 Steam 文件路径与当前 Steam 目录不一致");
        var expected = transaction["edited_sha256"]!.GetValue<string>(); var actual = FileOperations.Sha256(sourcePath);
        if (actual != expected && !force) throw new TransactionException($"当前文件已在编辑后发生变化（当前 {actual}，预期 {expected}），拒绝普通恢复");
        var backup = SafeRelative(store.DataDirectory, transaction["snapshot"]!.GetValue<string>());
        var originalHash = transaction["original_sha256"]!.GetValue<string>();
        if (!File.Exists(backup) || FileOperations.Sha256(backup) != originalHash) throw new IntegrityException($"编辑前备份 SHA-256 不匹配：{backup}");
        var rollback = Path.Combine(Path.GetDirectoryName(backup)!, $"restore-rollback-{Guid.NewGuid():N}.bin");
        var forced = Path.Combine(Path.GetDirectoryName(backup)!, $"forced-current-{Guid.NewGuid():N}.bin");
        FileOperations.CopyDurable(sourcePath, rollback);
        try
        {
            if (force && actual != expected) FileOperations.CopyDurable(sourcePath, forced);
            FileOperations.CopyDurable(backup, sourcePath);
            BinaryKeyValues.Preview(File.ReadAllBytes(sourcePath));
            var forcedValue = File.Exists(forced) ? Path.GetRelativePath(store.DataDirectory, forced).Replace('\\', '/') : null;
            try { store.MarkRestored(appId, transaction["id"]!.GetValue<string>(), DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), forcedValue); }
            catch { FileOperations.CopyDurable(rollback, sourcePath); throw; }
            return new JsonObject { ["app_id"] = appId, ["target"] = sourcePath, ["restored_sha256"] = originalHash,
                ["forced_archive"] = File.Exists(forced) ? forced : null, ["can_restore"] = store.Active(appId) is not null };
        }
        finally { RecycleBin.FileIfExists(rollback); }
    }

    public static void WriteZip(string path, string member, byte[] payload)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                using (var stream = archive.CreateEntry(member, CompressionLevel.Optimal).Open()) stream.Write(payload);
            FileOperations.ReplaceStaged(temporary, path);
        }
        finally { RecycleBin.FileIfExists(temporary); }
    }

    private static Dictionary<string, AchievementTranslation> LoadEdits(string path, string appId, string language, string sourceHash)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path)); var root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new UsageException("编辑内容必须使用 version 1");
        if (root.GetProperty("app_id").GetString() != appId) throw new UsageException("编辑内容的 App ID 与命令参数不一致");
        if (!string.Equals(root.GetProperty("source_sha256").GetString(), sourceHash, StringComparison.OrdinalIgnoreCase)) throw new IntegrityException("本地成就文件已变化，请重新加载后再输出");
        if (!string.Equals(root.GetProperty("target_language").GetString(), language, StringComparison.OrdinalIgnoreCase)) throw new UsageException("编辑内容的目标语言与命令参数不一致");
        var result = new Dictionary<string, AchievementTranslation>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var api = row.GetProperty("api_name").GetString()?.Trim() ?? "";
            if (api.Length == 0 || result.ContainsKey(api)) throw new UsageException($"编辑内容包含空白或重复成就 ID：{(api.Length == 0 ? "<空>" : api)}");
            result[api] = new AchievementTranslation(row.GetProperty("name").GetString() ?? throw new UsageException($"{api} 的名称必须是字符串"), row.GetProperty("description").GetString() ?? throw new UsageException($"{api} 的说明必须是字符串"));
        }
        return result;
    }

    private static IEnumerable<(string ApiName, BinaryKeyValuesNode Name, BinaryKeyValuesNode Description)> AchievementNodes(IEnumerable<BinaryKeyValuesNode> nodes)
    {
        foreach (var bits in BinaryKeyValues.Walk(nodes).Where(node => node.TypeId == 0 && node.Name == "bits"))
            foreach (var achievement in bits.Children.Where(node => node.TypeId == 0))
            {
                var api = BinaryKeyValues.FirstString(achievement, "name");
                var name = BinaryKeyValues.NestedObject(achievement, "display", "name");
                var description = BinaryKeyValues.NestedObject(achievement, "display", "desc");
                if (api.Length > 0 && name is not null && description is not null) yield return (api, name, description);
            }
    }

    private static int SetLanguageValue(BinaryKeyValuesNode node, string language, string value)
    {
        var matches = node.Children.Where(child => child.TypeId == 1 && child.Name.Equals(language, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new PreflightException($"目标语言 {language} 在同一字段中出现重复节点，拒绝编辑");
        var encoded = Encoding.UTF8.GetBytes(value);
        if (matches.Length == 1) { var changed = !matches[0].RawValue.AsSpan().SequenceEqual(encoded); matches[0].RawValue = encoded; matches[0].Value = value; return changed ? 1 : 0; }
        var added = new BinaryKeyValuesNode(1, language) { RawValue = encoded, Value = value }; node.Children.Add(added); return 1;
    }

    private static string ValidatePath(string path, string appId)
    {
        var full = Path.GetFullPath(path); var expected = $"UserGameStatsSchema_{appId}.bin";
        if (!ulong.TryParse(appId, out _) || Path.GetFileName(full) != expected) throw new UsageException($"schema 文件名必须是 {expected}");
        if (!File.Exists(full)) throw new PreflightException($"找不到本地成就文件：{full}");
        return full;
    }
    private static string ValidateLanguage(string value) { var language = value.Trim().ToLowerInvariant(); if (language is "token" or "tokens" || !LanguageRegex().IsMatch(language)) throw new UsageException($"无效的 Steam 语言代码：{value}"); return language; }
    private static void ValidateText(string value, string api, string label) { if (value.Any(character => character is '\0' or '\r' or '\n' or '\t' || character < 32)) throw new UsageException($"{api} 的{label}包含 NUL、换行、制表符或控制字符"); }
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray());
    private static string SafeRelative(string root, string value) { var full = Path.GetFullPath(Path.Combine(root, value)); if (Path.GetRelativePath(root, full).StartsWith("..", StringComparison.Ordinal)) throw new TransactionException("编辑历史中的备份路径越界"); return full; }
    [GeneratedRegex("^[a-z][a-z0-9_]{1,31}$", RegexOptions.CultureInvariant)] private static partial Regex LanguageRegex();
}
