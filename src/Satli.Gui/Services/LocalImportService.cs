using System.Text.Json;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

public sealed record LocalImportPreview(
    ReplacementPreview Replacement,
    string SchemaSha256,
    string SourcePath);

public sealed class LocalImportService
{
    private readonly SatliCliService _cli = new();

    public async Task<LocalImportPreview> PreviewAsync(string sourcePath, GuiSettings settings)
    {
        var result = await RunAsync(
            BuildArguments(sourcePath, settings, dryRun: true, expectedSha256: null),
            settings,
            "正在校验本地翻译并读取成就内容…");
        return ParsePreview(result);
    }

    public async Task InstallAsync(
        string sourcePath,
        string expectedSha256,
        GuiSettings settings)
    {
        await RunAsync(
            BuildArguments(sourcePath, settings, dryRun: false, expectedSha256),
            settings,
            "正在备份并安装本地翻译…");
    }

    public static LocalImportPreview ParsePreview(CliRunResult result)
    {
        var payloads = result.Events
            .Where(item => item.Operation == "local-import" && item.Event == "item-preview")
            .Select(item => item.Payload)
            .ToList();
        if (payloads.Count != 1)
        {
            throw new InvalidDataException(
                $"本地导入预览数量无效：期望 1 个，收到 {payloads.Count} 个。拒绝继续。"
            );
        }

        var payload = payloads[0];
        var sha256 = GetRequiredString(payload, "schema_sha256");
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("本地导入预览返回了无效的 schema SHA-256。拒绝继续。");
        }
        var source = GetRequiredString(payload, "source");
        var replacement = ReplacementPreview.FromPayload(payload, "本地翻译");
        if (string.IsNullOrWhiteSpace(replacement.AppId) || replacement.Rows.Count == 0)
        {
            throw new InvalidDataException("本地导入预览缺少 App ID 或成就内容。拒绝继续。");
        }
        return new LocalImportPreview(replacement, sha256.ToLowerInvariant(), source);
    }

    private async Task<CliRunResult> RunAsync(
        IReadOnlyList<string> arguments,
        GuiSettings settings,
        string status)
    {
        await App.Logs.WriteAsync("信息", "本地导入", status);
        var result = await _cli.RunAsync(
            arguments,
            networkSettings: settings.Network,
            steamLibrarySettings: settings.SteamLibrary,
            downloadSourceSettings: settings.DownloadSources);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "本地导入命令执行失败。"
                    : result.ErrorMessage);
        }
        return result;
    }

    private static List<string> BuildArguments(
        string sourcePath,
        GuiSettings settings,
        bool dryRun,
        string? expectedSha256)
    {
        var arguments = new List<string> { "local-import", sourcePath };
        if (dryRun)
        {
            arguments.AddRange(["--dry-run", "--preview-content"]);
        }
        else
        {
            arguments.Add("--yes");
            arguments.AddRange(["--expected-sha256", expectedSha256!]);
        }
        arguments.Add("--jsonl");
        CliConfiguredPaths.AppendDataDirectory(arguments, settings);
        CliConfiguredPaths.AppendSteamDirectory(
            arguments,
            settings,
            App.ViewModel.Translations.DetectedSteamDirectory);
        return arguments;
    }

    private static string GetRequiredString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"本地导入预览缺少 {propertyName}。拒绝继续。");
        }
        return value.GetString()!;
    }
}
