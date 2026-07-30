using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed class SchemaRevisionService
{
    private readonly SatlCliService _cli = new();

    public async Task<IReadOnlyList<SchemaRevisionItem>> ListAsync(GameItem game)
    {
        var result = await RunAsync(["schema", "revisions", "list", game.AppId, "--jsonl"]);
        return result.Events
            .Where(item => item.Operation == "schema-revisions-list" && item.Event == "item-succeeded")
            .Select(item => SchemaRevisionItem.FromPayload(item.Payload))
            .ToArray();
    }

    public async Task<SchemaRevisionDiff> PreviewDiffAsync(
        GameItem game,
        SchemaRevisionItem revision)
    {
        var payload = await ShowPayloadAsync(game, revision);
        var current = RevisionPreview(payload.GetProperty("preview"), game, revision);
        if (string.IsNullOrWhiteSpace(revision.ParentSchemaSha256))
        {
            return new SchemaRevisionDiff(null, current);
        }

        var parent = (await ListAsync(game)).FirstOrDefault(item =>
            item.IsAvailable
            && item.SchemaSha256.Equals(
                revision.ParentSchemaSha256,
                StringComparison.OrdinalIgnoreCase));
        if (parent is null)
        {
            throw new InvalidDataException("找不到此修订记录的父内容，无法生成 Git 差异预览。");
        }
        var parentPayload = await ShowPayloadAsync(game, parent);
        return new SchemaRevisionDiff(
            RevisionPreview(parentPayload.GetProperty("preview"), game, parent),
            current);
    }

    public async Task<IReadOnlyList<ReplacementPreview>> CompareAsync(
        GameItem game,
        SchemaRevisionItem revision)
    {
        var payload = await ShowPayloadAsync(game, revision);
        var target = RevisionPreview(payload.GetProperty("preview"), game, revision);
        if (!payload.TryGetProperty("current_preview", out var currentPayload))
        {
            return [target];
        }
        var current = ReplacementPreview.FromPayload(currentPayload, game.GameName) with
        {
            AppId = game.AppId,
            GameName = game.GameName,
            VariantId = "当前文件",
            Action = "replace",
        };
        return [current, target];
    }

    public Task ExportAsync(
        GameItem game,
        SchemaRevisionItem revision,
        string format,
        string output) =>
        RunAsync(
            [
                "schema", "revisions", "export", game.AppId, revision.Commit,
                "--format", format,
                "--output", output,
                "--jsonl",
            ]);

    public Task ActivateAsync(GameItem game, SchemaRevisionItem revision, bool force)
    {
        var arguments = new List<string>
        {
            "schema", "revisions", "activate", game.AppId, revision.Commit,
            "--yes", "--jsonl",
        };
        if (force)
        {
            arguments.Add("--force");
        }
        return RunAsync(arguments);
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
                    ? $"SATL 修订操作失败，退出码 {result.ExitCode}。"
                    : result.ErrorMessage);
        }
        return result;
    }

    private async Task<System.Text.Json.JsonElement> ShowPayloadAsync(
        GameItem game,
        SchemaRevisionItem revision)
    {
        var result = await RunAsync(
            ["schema", "revisions", "show", game.AppId, revision.Commit, "--jsonl"]);
        return RequiredPayload(result, "schema-revisions-show");
    }

    private static ReplacementPreview RevisionPreview(
        System.Text.Json.JsonElement previewPayload,
        GameItem game,
        SchemaRevisionItem revision) =>
        ReplacementPreview.FromPayload(previewPayload, game.GameName) with
        {
            AppId = game.AppId,
            GameName = game.GameName,
            VariantId = $"目标修订 {revision.ShortCommit}",
            Action = "replace",
        };

    private static System.Text.Json.JsonElement RequiredPayload(
        CliRunResult result,
        string operation) =>
        result.Events.LastOrDefault(item =>
            item.Operation == operation && item.Event == "item-succeeded")?.Payload
        ?? throw new InvalidDataException($"SATL 未返回 {operation} 结果。");

    private static void AddConfiguredPaths(List<string> arguments)
    {
        var settings = App.ViewModel.Settings;
        if (!string.IsNullOrWhiteSpace(settings.SteamDirectory))
        {
            arguments.AddRange(["--steam-dir", settings.SteamDirectory]);
        }
        if (!string.IsNullOrWhiteSpace(settings.DataDirectory))
        {
            arguments.AddRange(["--data-dir", settings.DataDirectory]);
        }
    }
}
