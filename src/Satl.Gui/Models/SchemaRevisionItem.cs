using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace Satl_Gui.Models;

public sealed class SchemaRevisionItem
{
    public string Commit { get; set; } = string.Empty;
    public string ShortCommit { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string SchemaSha256 { get; set; } = string.Empty;
    public string ParentSchemaSha256 { get; set; } = string.Empty;
    public int AchievementCount { get; set; }
    public int ChangedNames { get; set; }
    public int ChangedDescriptions { get; set; }
    public string VariantId { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public bool IsAvailable { get; set; } = true;

    public string ActionText => Action switch
    {
        "draft" => "自动保存草稿",
        "apply" => "保存到本机",
        "export" => "导出",
        "restore" => "恢复编辑",
        "activate" => "设为当前",
        "legacy-import" => "导入旧历史",
        "legacy-unavailable" => "旧记录不可用",
        _ => Action,
    };

    public string TitleText
    {
        get
        {
            if (DateTimeOffset.TryParse(
                    CreatedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var created))
            {
                return created.ToLocalTime().ToString("yyyy年M月d日 HH:mm:ss", CultureInfo.CurrentCulture);
            }
            return string.IsNullOrWhiteSpace(CreatedAt) ? "未知时间" : CreatedAt;
        }
    }

    public string SummaryText =>
        $"{ActionText} · {TargetLanguage} · {AchievementCount} 个成就";

    public string HashText => IsAvailable
        ? $"commit {ShortCommit} · SHA-256 {SchemaSha256}"
        : $"成品已丢失 · 原记录 SHA-256 {SchemaSha256}";
    public bool CanPreview => IsAvailable;
    public bool CanActivate => IsAvailable && !IsCurrent;
    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public static SchemaRevisionItem FromPayload(JsonElement payload) => new()
    {
        Commit = GetString(payload, "commit"),
        ShortCommit = GetString(payload, "short_commit"),
        AppId = GetString(payload, "app_id"),
        GameName = GetString(payload, "game_name"),
        TargetLanguage = GetString(payload, "target_language"),
        Action = GetString(payload, "action"),
        CreatedAt = GetString(payload, "created_at"),
        SchemaSha256 = GetString(payload, "schema_sha256"),
        ParentSchemaSha256 = GetString(payload, "parent_schema_sha256"),
        AchievementCount = GetInt(payload, "achievement_count"),
        ChangedNames = GetInt(payload, "changed_names"),
        ChangedDescriptions = GetInt(payload, "changed_descriptions"),
        VariantId = GetString(payload, "variant_id"),
        IsCurrent = payload.TryGetProperty("is_current", out var current) && current.GetBoolean(),
        IsAvailable = !payload.TryGetProperty("available", out var available)
            || available.GetBoolean(),
    };

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;
}
