using System.Text.Json;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed class SchemaEditorService
{
    private readonly SatlCliService _cli;

    public SchemaEditorService(SatlCliService? cli = null)
    {
        _cli = cli ?? new SatlCliService();
    }

    public async Task<SchemaInspection> InspectAsync(GameItem game)
    {
        var result = await RunAsync(["schema", "inspect", game.AppId, "--jsonl"]);
        var payload = RequiredPayload(result, "item-succeeded");
        var rows = new List<AchievementEditorRow>();
        foreach (var row in payload.GetProperty("rows").EnumerateArray())
        {
            var translations = new Dictionary<string, EditorTranslation>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var translation in row.GetProperty("translations").EnumerateObject())
            {
                translations[translation.Name] = new EditorTranslation(
                    translation.Value.GetProperty("name").GetString() ?? string.Empty,
                    translation.Value.GetProperty("description").GetString() ?? string.Empty);
            }
            rows.Add(new AchievementEditorRow
            {
                Index = row.GetProperty("index").GetInt32(),
                ApiName = row.GetProperty("api_name").GetString() ?? string.Empty,
                Translations = translations,
            });
        }
        return new SchemaInspection(
            game.AppId,
            payload.GetProperty("source_path").GetString() ?? string.Empty,
            payload.GetProperty("source_sha256").GetString() ?? string.Empty,
            payload.TryGetProperty("can_restore", out var canRestore) && canRestore.GetBoolean(),
            payload.GetProperty("languages").EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray(),
            rows,
            game.GameName,
            game.InstalledVariantId);
    }

    public Task<SchemaEditResult> ApplyAsync(
        SchemaInspection inspection,
        string targetLanguage,
        IReadOnlyList<AchievementEditorRow> rows,
        bool allowIncomplete) =>
        RunEditCommandAsync(
            inspection,
            targetLanguage,
            rows,
            [
                "schema", "apply", inspection.AppId,
                "--game-name", inspection.GameName,
                "--variant-id", inspection.VariantId,
                "--yes", "--jsonl",
            ],
            allowIncomplete);

    public Task<SchemaEditResult> ExportAsync(
        SchemaInspection inspection,
        string targetLanguage,
        IReadOnlyList<AchievementEditorRow> rows,
        bool allowIncomplete,
        string format,
        string output) =>
        RunEditCommandAsync(
            inspection,
            targetLanguage,
            rows,
            [
                "schema", "export", inspection.AppId,
                "--format", format,
                "--output", output,
                "--game-name", inspection.GameName,
                "--variant-id", inspection.VariantId,
                "--jsonl",
            ],
            allowIncomplete);

    public Task<SchemaEditResult> RecordDraftAsync(
        SchemaInspection inspection,
        string targetLanguage,
        IReadOnlyList<AchievementEditorRow> rows) =>
        RunEditCommandAsync(
            inspection,
            targetLanguage,
            rows,
            [
                "schema", "draft", inspection.AppId,
                "--game-name", inspection.GameName,
                "--variant-id", inspection.VariantId,
                "--jsonl",
            ],
            allowIncomplete: true);

    public async Task<SchemaEditResult> RestoreAsync(string appId, bool force)
    {
        var arguments = new List<string> { "schema", "restore", appId, "--yes", "--jsonl" };
        if (force)
        {
            arguments.Add("--force");
        }
        var result = await RunAsync(arguments);
        var payload = RequiredPayload(result, "item-succeeded");
        return ParseResult(payload);
    }

    private async Task<SchemaEditResult> RunEditCommandAsync(
        SchemaInspection inspection,
        string targetLanguage,
        IReadOnlyList<AchievementEditorRow> rows,
        List<string> arguments,
        bool allowIncomplete)
    {
        var temporary = Path.Combine(
            Path.GetTempPath(),
            "SteamAchievementTranslationInstaller",
            $"schema-edits-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
        try
        {
            var edits = new
            {
                version = 1,
                app_id = inspection.AppId,
                source_sha256 = inspection.SourceSha256,
                target_language = targetLanguage,
                rows = rows.Select(row => new
                {
                    api_name = row.ApiName,
                    name = row.TargetName,
                    description = row.TargetDescription,
                }),
            };
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(edits, new JsonSerializerOptions { WriteIndented = true }));
            arguments.AddRange(["--target-language", targetLanguage, "--edits-file", temporary]);
            if (allowIncomplete)
            {
                arguments.Add("--allow-incomplete");
            }
            var result = await RunAsync(arguments);
            return ParseResult(RequiredPayload(result, "item-succeeded"));
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task<CliRunResult> RunAsync(List<string> arguments)
    {
        AddConfiguredPaths(arguments);
        var result = await _cli.RunAsync(
            arguments,
            networkSettings: App.ViewModel.Settings.Network,
            downloadSourceSettings: App.ViewModel.Settings.DownloadSources);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? $"SATL schema 操作失败，退出码 {result.ExitCode}。"
                    : result.ErrorMessage);
        }
        return result;
    }

    private static JsonElement RequiredPayload(CliRunResult result, string eventName) =>
        result.Events.LastOrDefault(item => item.Event == eventName)?.Payload
        ?? throw new InvalidDataException($"SATL schema 操作未返回 {eventName} 事件。");

    private static SchemaEditResult ParseResult(JsonElement payload) => new(
        payload.TryGetProperty("output_sha256", out var outputHash)
            ? outputHash.GetString() ?? string.Empty
            : payload.TryGetProperty("restored_sha256", out var restoredHash)
                ? restoredHash.GetString() ?? string.Empty
                : string.Empty,
        payload.TryGetProperty("achievement_count", out var count) ? count.GetInt32() : 0,
        payload.TryGetProperty("changed_fields", out var changed) ? changed.GetInt32() : 0,
        payload.TryGetProperty("missing_names", out var missingNames) ? missingNames.GetInt32() : 0,
        payload.TryGetProperty("missing_descriptions", out var missingDescriptions)
            ? missingDescriptions.GetInt32()
            : 0,
        payload.TryGetProperty("can_restore", out var canRestore) && canRestore.GetBoolean(),
        payload.TryGetProperty("output", out var output) ? output.GetString() : null,
        payload.TryGetProperty("backup", out var backup) ? backup.GetString() : null,
        payload.TryGetProperty("changed_names", out var changedNames) ? changedNames.GetInt32() : 0,
        payload.TryGetProperty("changed_descriptions", out var changedDescriptions)
            ? changedDescriptions.GetInt32()
            : 0,
        payload.TryGetProperty("complete_languages", out var languages)
            ? languages.EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray()
            : [],
        payload.TryGetProperty("revision_commit", out var revision)
            ? revision.GetString() ?? string.Empty
            : string.Empty,
        payload.TryGetProperty("revision_warning", out var warning)
            ? warning.GetString() ?? string.Empty
            : string.Empty);

    private static void AddConfiguredPaths(List<string> arguments)
    {
        var settings = App.ViewModel.Settings;
        CliConfiguredPaths.AppendSteamDirectory(
            arguments,
            settings,
            App.ViewModel.Translations.DetectedSteamDirectory);
        CliConfiguredPaths.AppendDataDirectory(arguments, settings);
    }
}
