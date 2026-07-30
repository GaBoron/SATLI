using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Satl_Gui.Models;
using Windows.System;

namespace Satl_Gui.Services;

public sealed record ContributionDraft(
    string ZipPath,
    Uri IssueFormUri,
    bool IsUpdate,
    string Languages,
    string Summary);

public sealed class ContributionWorkflowService
{
    private const string IssueBase =
        "https://github.com/GaBoron/steam-achievement-translation-library/issues/new";

    public ContributionDraft Prepare(GameItem game, SchemaEditResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidDataException("投稿 ZIP 路径为空。");
        }
        var zipPath = Path.GetFullPath(result.Output);
        var expectedName = $"UserGameStatsSchema_{game.AppId}.zip";
        if (!Path.GetFileName(zipPath).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"投稿文件必须命名为 {expectedName}。");
        }
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("找不到刚导出的投稿 ZIP。", zipPath);
        }

        var schema = ReadSingleSchema(zipPath, game.AppId);
        var schemaSha256 = Convert.ToHexString(SHA256.HashData(schema)).ToLowerInvariant();
        if (!schemaSha256.Equals(result.OutputSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("投稿 ZIP 内 schema 的 SHA-256 与导出结果不一致。");
        }
        var languages = (result.CompleteLanguages ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (languages.Length == 0)
        {
            throw new InvalidDataException("投稿文件没有任何名称和说明都完整的语言。");
        }

        var isUpdate = game.Variants.Count > 0;
        var summary = result.ChangedNames == 0 && result.ChangedDescriptions == 0
            ? "重新导出并校验现有译本。"
            : $"修正 {result.ChangedNames} 项成就名称和 {result.ChangedDescriptions} 项成就说明。";
        var fields = new Dictionary<string, string>
        {
            ["template"] = isUpdate
                ? "translation_update_zh.yml"
                : "translation_contribution_zh.yml",
            ["title"] = $"[{(isUpdate ? "翻译更新" : "翻译投稿")}] {game.GameName} ({game.AppId})",
            ["game_name"] = game.GameName,
            ["app_id"] = game.AppId,
            ["store_url"] = $"https://store.steampowered.com/app/{game.AppId}/",
            ["languages"] = string.Join(", ", languages),
            ["notes"] = $"由 SATL 校验并导出；schema SHA-256：{schemaSha256}",
        };
        if (isUpdate)
        {
            fields["update_summary"] = summary;
            if (game.Variants.Count > 1 && !string.IsNullOrWhiteSpace(game.SelectedVariantId))
            {
                fields["variant_id"] = game.SelectedVariantId;
            }
        }
        var query = string.Join(
            "&",
            fields.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new ContributionDraft(
            zipPath,
            new Uri($"{IssueBase}?{query}"),
            isUpdate,
            string.Join(", ", languages),
            summary);
    }

    public async Task OpenAsync(ContributionDraft draft)
    {
        if (!await Launcher.LaunchUriAsync(draft.IssueFormUri))
        {
            throw new InvalidOperationException("系统未能打开 GitHub 投稿表单。");
        }
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add($"/select,{draft.ZipPath}");
        Process.Start(startInfo);
    }

    private static byte[] ReadSingleSchema(string zipPath, string appId)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var expectedMember = $"UserGameStatsSchema_{appId}.bin";
        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (files.Length != 1
            || !files[0].FullName.Equals(expectedMember, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"投稿 ZIP 必须只包含根目录下的 {expectedMember}。");
        }
        using var stream = files[0].Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
