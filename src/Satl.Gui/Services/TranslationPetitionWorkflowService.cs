using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Satl_Gui.Models;
using Windows.System;

namespace Satl_Gui.Services;

public sealed record TranslationPetitionInput(
    string GameName,
    string AppId,
    string TargetLanguages,
    string Notes);

public sealed record TranslationPetitionDraft(
    string ZipPath,
    Uri IssueFormUri,
    TranslationPetitionInput Input);

public sealed class TranslationPetitionWorkflowService
{
    private const string IssueBase =
        "https://github.com/GaBoron/steam-achievement-translation-library/issues/new";

    public TranslationPetitionInput NormalizeInput(TranslationPetitionInput input)
    {
        var gameName = SingleLine(input.GameName);
        var appId = input.AppId.Trim();
        if (string.IsNullOrWhiteSpace(gameName))
        {
            throw new ArgumentException("请填写 Steam 商店中的游戏名。");
        }
        if (appId.Length > 20
            || !ulong.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed == 0)
        {
            throw new ArgumentException("Steam App ID 必须是有效的正整数。");
        }

        var languages = input.TargetLanguages
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(language => language.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (languages.Length == 0
            || languages.Any(language => language.Length > 32 || !language.All(IsLanguageCodeCharacter)))
        {
            throw new ArgumentException("目标语言必须使用 Steam 语言代码，多个代码用逗号分隔。");
        }

        return new TranslationPetitionInput(
            gameName,
            appId,
            string.Join(", ", languages),
            input.Notes.Trim());
    }

    public TranslationPetitionDraft Prepare(
        TranslationPetitionInput input,
        string zipPath)
    {
        var normalized = NormalizeInput(input);
        var fullPath = Path.GetFullPath(zipPath);
        var expectedName = $"UserGameStatsSchema_{normalized.AppId}.zip";
        if (!Path.GetFileName(fullPath).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"请愿文件必须命名为 {expectedName}。");
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到刚导出的请愿 ZIP。", fullPath);
        }

        var schema = SchemaZipArchive.ReadSingleSchema(fullPath, normalized.AppId);
        var sha256 = Convert.ToHexString(SHA256.HashData(schema)).ToLowerInvariant();
        var generatedNote = $"由 SATL 校验并导出；schema SHA-256：{sha256}";
        var notes = string.IsNullOrWhiteSpace(normalized.Notes)
            ? generatedNote
            : $"{normalized.Notes}{Environment.NewLine}{Environment.NewLine}{generatedNote}";
        var fields = new Dictionary<string, string>
        {
            ["template"] = "translation_petition_zh.yml",
            ["title"] = $"[翻译请愿] {normalized.GameName} ({normalized.AppId})",
            ["game_name"] = normalized.GameName,
            ["app_id"] = normalized.AppId,
            ["store_url"] = $"https://store.steampowered.com/app/{normalized.AppId}/",
            ["target_languages"] = normalized.TargetLanguages,
            ["notes"] = notes,
        };
        var query = string.Join(
            "&",
            fields.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new TranslationPetitionDraft(
            fullPath,
            new Uri($"{IssueBase}?{query}"),
            normalized);
    }

    public async Task OpenAsync(TranslationPetitionDraft draft)
    {
        if (!await Launcher.LaunchUriAsync(draft.IssueFormUri))
        {
            throw new InvalidOperationException("系统未能打开 GitHub 翻译请愿表单。");
        }
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"/select,{draft.ZipPath}");
        Process.Start(startInfo);
    }

    private static bool IsLanguageCodeCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '-';

    private static string SingleLine(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}
