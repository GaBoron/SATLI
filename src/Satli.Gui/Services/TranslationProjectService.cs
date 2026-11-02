using System.Text.Json.Nodes;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Satli.Core.TranslationProjects;
using Satli_Gui.Models;
using AchievementTranslation = Satli.Core.Formats.AchievementTranslation;

namespace Satli_Gui.Services;

public sealed class TranslationProjectService
{
    public static TranslationProjectStore Store() => new(Path.Combine(
        LiteWorkflowService.DataDirectory(App.ViewModel.Settings), "translation-projects"));

    public static SchemaInspection Inspect(TranslationProject project)
    {
        var source = project.Build(forPublication: false);
        var rows = TranslationProject.ReadRows(source).Select(row => new AchievementEditorRow
        {
            Index = row.Index, ApiName = row.ApiName,
            Translations = row.Translations.ToDictionary(pair => pair.Key,
                pair => new EditorTranslation(pair.Value.Name, pair.Value.Description), StringComparer.Ordinal),
        }).ToArray();
        return new(project.AppId, project.Source, project.SourceFingerprint, false,
            source["languages"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray(), rows,
            project.GameName, project.VariantId, project);
    }

    public static async Task SaveAsync(SchemaInspection inspection, string language, IReadOnlyList<AchievementEditorRow> rows)
    {
        var project = inspection.Project ?? throw new InvalidOperationException();
        if (project.TargetLanguage != language)
            throw new InvalidOperationException("目标语言与工程不一致。");
        project.Edits = rows.ToDictionary(row => row.ApiName,
            row => new AchievementTranslation(row.TargetName, row.TargetDescription), StringComparer.Ordinal);
        await Store().SaveAsync(project);
    }

    public static async Task<SchemaEditResult> ExportAsync(SchemaInspection inspection, string language,
        IReadOnlyList<AchievementEditorRow> rows, string output, string variantId)
    {
        await SaveAsync(inspection, language, rows);
        return await Task.Run(() =>
        {
            if (Path.GetFileName(output) != $"UserGameStatsSchema_{inspection.AppId}.json")
                throw new InvalidDataException("JSON 文件名与 App ID 不一致。");
            var json = inspection.Project!.Build(forPublication: true);
            json["variant_id"] = variantId;
            var bytes = TranslationJsonExport.Serialize(json);
            TranslationJson.Parse(bytes, inspection.AppId);
            FileOperations.WriteDurable(output, bytes);
            return new SchemaEditResult(FileOperations.Sha256(bytes), rows.Count, 0, 0, 0, false, output, null,
                SubmissionLanguages: json["languages"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        });
    }

    public static async Task<TranslationProject> ImportAsync(string path, string gameName, string target)
    {
        if (new FileInfo(path).Length > TranslationJson.MaximumBytes) throw new InvalidDataException("JSON 超过 32 MiB。");
        var bytes = await File.ReadAllBytesAsync(path);
        return await Task.Run(() =>
        {
            var appId = JsonNode.Parse(bytes)?["app_id"]?.GetValue<string>() ?? "";
            var json = TranslationJson.Parse(bytes, appId, allowIncomplete: true);
            return TranslationProject.Create(json, string.IsNullOrWhiteSpace(gameName) ? $"Steam 游戏 {appId}" : gameName,
                target, Path.GetFileName(path));
        });
    }
}
