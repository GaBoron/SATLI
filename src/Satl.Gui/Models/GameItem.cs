using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Satl_Gui.Models;

public sealed class SchemaVariantOption
{
    public required string VariantId { get; init; }
    public bool Primary { get; init; }
    public string Note { get; init; } = string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(Note) ? VariantId : $"{VariantId} · {Note}";
    public override string ToString() => DisplayName;
}

public sealed class GameItem : ObservableObject
{
    private bool _isSelected;
    private SchemaVariantOption? _selectedVariant;
    private string _selectedVariantId = string.Empty;
    private string _installedState = "unmanaged";
    private string _installedVariantId = string.Empty;
    private string _installedSource = string.Empty;

    public required string AppId { get; init; }
    public required string GameName { get; init; }
    public string CatalogStatus { get; init; } = "unknown";
    public string DiscoveryText { get; init; } = string.Empty;
    public IReadOnlyList<string> NativeLanguages { get; init; } = [];
    public ObservableCollection<SchemaVariantOption> Variants { get; } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public SchemaVariantOption? SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            var normalized = value
                ?? Variants.FirstOrDefault(item => item.Primary)
                ?? Variants.FirstOrDefault();
            if (SetProperty(ref _selectedVariant, normalized))
            {
                var variantId = normalized?.VariantId ?? string.Empty;
                if (_selectedVariantId != variantId)
                {
                    _selectedVariantId = variantId;
                    OnPropertyChanged(nameof(SelectedVariantId));
                }
            }
        }
    }

    public string SelectedVariantId
    {
        get => _selectedVariantId;
        set
        {
            var selected = Variants.FirstOrDefault(item => item.VariantId == value)
                ?? Variants.FirstOrDefault(item => item.Primary)
                ?? Variants.FirstOrDefault();
            var normalized = selected?.VariantId ?? string.Empty;
            var idChanged = SetProperty(ref _selectedVariantId, normalized);
            if (!ReferenceEquals(_selectedVariant, selected))
            {
                _selectedVariant = selected;
                OnPropertyChanged(nameof(SelectedVariant));
            }
            else if (!idChanged)
            {
                return;
            }
        }
    }

    public string InstalledState
    {
        get => _installedState;
        set
        {
            if (SetProperty(ref _installedState, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(InstalledVersionText));
                OnPropertyChanged(nameof(ManagedSummaryText));
                OnPropertyChanged(nameof(CanViewInstalledTranslation));
                OnPropertyChanged(nameof(CanRestore));
                OnPropertyChanged(nameof(RequiresForceRestore));
                OnPropertyChanged(nameof(RestoreActionText));
            }
        }
    }

    public string InstalledVariantId
    {
        get => _installedVariantId;
        set
        {
            if (SetProperty(ref _installedVariantId, value))
            {
                OnPropertyChanged(nameof(InstalledVersionText));
                OnPropertyChanged(nameof(ManagedSummaryText));
            }
        }
    }

    public string InstalledSource
    {
        get => _installedSource;
        set
        {
            if (SetProperty(ref _installedSource, value))
            {
                OnPropertyChanged(nameof(IsLocalImport));
                OnPropertyChanged(nameof(InstalledSourceText));
                OnPropertyChanged(nameof(ManagedSummaryText));
                OnPropertyChanged(nameof(CatalogText));
                OnPropertyChanged(nameof(HasCatalogWarning));
                OnPropertyChanged(nameof(CatalogWarningText));
            }
        }
    }

    public string InstalledAt { get; init; } = string.Empty;
    public string InstalledSha256 { get; init; } = string.Empty;

    public string StateText => InstalledState switch
    {
        "installed" => "已安装",
        "modified" => "已被修改",
        "missing" => "文件缺失",
        "restored" => "已恢复",
        "unreadable" => "无法读取",
        _ => "未管理",
    };

    public bool IsModified => InstalledState == "modified";
    public bool IsLocalImport => InstalledSource == "local-import"
        || InstalledVariantId.StartsWith("local-", StringComparison.OrdinalIgnoreCase);
    public bool CanViewInstalledTranslation => InstalledState is "installed" or "modified";
    public bool CanRestore => InstalledState is "installed" or "modified" or "missing";
    public bool RequiresForceRestore => InstalledState is "modified" or "missing";
    public string RestoreActionText => RequiresForceRestore ? "强制恢复" : "恢复";
    public string InstalledSourceText => IsLocalImport
        ? "来源：本地导入"
        : InstalledSource == "catalog"
            ? "来源：社区翻译库"
            : "来源：历史安装记录";
    public string InstalledVersionText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstalledVariantId) || InstalledState is "unmanaged" or "restored")
            {
                return "已安装版本：无";
            }
            var variant = Variants.FirstOrDefault(item => item.VariantId == InstalledVariantId);
            return $"已安装版本：{variant?.DisplayName ?? InstalledVariantId}";
        }
    }
    public string ManagedSummaryText => $"{InstalledSourceText} · {InstalledVersionText}";
    public bool IsCurrent => CatalogStatus == "current";
    public bool HasNativeChinese => NativeLanguages.Any(language =>
        language.Equals("schinese", StringComparison.OrdinalIgnoreCase)
        || language.Equals("tchinese", StringComparison.OrdinalIgnoreCase));
    public bool HasCatalogWarning => !IsLocalImport && !IsCurrent && !HasNativeChinese;
    public string CatalogText => IsLocalImport
        ? "本地导入译本"
        : HasNativeChinese
        ? "本游戏自带中文"
        : $"索引状态：{CatalogStatusPresentation.Label(CatalogStatus)}";
    public string CatalogWarningText => IsLocalImport || HasNativeChinese
        ? string.Empty
        : CatalogStatusPresentation.Warning(CatalogStatus);
    public string Subtitle => $"App ID {AppId}" + (string.IsNullOrWhiteSpace(DiscoveryText) ? string.Empty : $" · {DiscoveryText}");

    public static GameItem FromPayload(JsonElement payload)
    {
        var item = new GameItem
        {
            AppId = GetString(payload, "app_id", "0"),
            GameName = GetString(payload, "game_name", "未知游戏"),
            CatalogStatus = GetString(payload, "catalog_status", "unknown"),
            DiscoveryText = payload.TryGetProperty("discovery", out var discovery)
                ? string.Join(
                    " / ",
                    discovery.EnumerateArray()
                        .Select(source => source.GetString())
                        .Where(source => !string.IsNullOrWhiteSpace(source))
                        .Select(source => DiscoveryLabel(source!)))
                : string.Empty,
            NativeLanguages = GetStringArray(payload, "native_languages"),
            InstalledState = GetString(payload, "installed_state", "unmanaged"),
            InstalledVariantId = GetString(payload, "installed_variant_id", string.Empty),
            InstalledSource = GetString(payload, "installed_source", string.Empty),
            InstalledAt = GetString(payload, "installed_at", string.Empty),
            InstalledSha256 = GetString(payload, "installed_sha256", string.Empty),
        };

        if (payload.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Array)
        {
            foreach (var variant in variants.EnumerateArray())
            {
                item.Variants.Add(new SchemaVariantOption
                {
                    VariantId = GetString(variant, "variant_id", "default"),
                    Primary = variant.TryGetProperty("primary", out var primary) && primary.GetBoolean(),
                    Note = GetString(variant, "note_zh", string.Empty),
                });
            }
        }

        item.SelectedVariantId = (
            item.Variants.FirstOrDefault(variant => variant.VariantId == item.InstalledVariantId)
            ?? item.Variants.FirstOrDefault(variant => variant.Primary)
            ?? item.Variants.FirstOrDefault())?.VariantId ?? string.Empty;
        return item;
    }

    private static string GetString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray()
            : [];

    private static string DiscoveryLabel(string source) => source switch
    {
        "installed" => "已安装",
        "account-cache" => "账号缓存",
        "steam-web-api" => "Steam Web API",
        _ => source,
    };
}
