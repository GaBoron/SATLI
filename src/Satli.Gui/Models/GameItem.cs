using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Satli_Gui.Models;

public sealed class SchemaVariantOption
{
    public required string VariantId { get; init; }
    public bool Primary { get; init; }
    public string Note { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
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
    private string _installedAt = string.Empty;
    private string _installedSha256 = string.Empty;

    public required string AppId { get; init; }
    public required string GameName { get; init; }
    public string CatalogStatus { get; init; } = "unknown";
    public string DiscoveryText { get; init; } = string.Empty;
    public IReadOnlyList<string> Contributors { get; init; } = [];
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
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(UpdateVisibility));
                OnPropertyChanged(nameof(NeedsAttention));
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
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(UpdateVisibility));
                OnPropertyChanged(nameof(NeedsAttention));
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
                OnPropertyChanged(nameof(IsLocalEdit));
                OnPropertyChanged(nameof(InstalledSourceText));
                OnPropertyChanged(nameof(ManagedSummaryText));
                OnPropertyChanged(nameof(CatalogText));
                OnPropertyChanged(nameof(HasCatalogWarning));
                OnPropertyChanged(nameof(CatalogWarningText));
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(UpdateVisibility));
                OnPropertyChanged(nameof(NeedsAttention));
            }
        }
    }

    public string InstalledAt
    {
        get => _installedAt;
        set => SetProperty(ref _installedAt, value);
    }

    public string InstalledSha256
    {
        get => _installedSha256;
        set
        {
            if (SetProperty(ref _installedSha256, value))
            {
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(UpdateVisibility));
            }
        }
    }

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
        || (string.IsNullOrWhiteSpace(InstalledSource)
            && InstalledVariantId.StartsWith("local-", StringComparison.OrdinalIgnoreCase)
            && !InstalledVariantId.StartsWith("local-edit-", StringComparison.OrdinalIgnoreCase));
    public bool IsLocalEdit => InstalledSource == "local-edit";
    public bool CanViewInstalledTranslation => InstalledState is "installed" or "modified";
    public bool CanRestore => InstalledState is "installed" or "modified" or "missing";
    public bool RequiresForceRestore => InstalledState is "modified" or "missing";
    public bool IsUpdateAvailable
    {
        get
        {
            if (InstalledState != "installed"
                || InstalledSource != "catalog"
                || string.IsNullOrWhiteSpace(InstalledVariantId)
                || string.IsNullOrWhiteSpace(InstalledSha256))
            {
                return false;
            }
            var catalogVariant = Variants.FirstOrDefault(item =>
                item.VariantId == InstalledVariantId);
            return catalogVariant is not null
                && !string.IsNullOrWhiteSpace(catalogVariant.Sha256)
                && !catalogVariant.Sha256.Equals(
                    InstalledSha256,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
    public Visibility UpdateVisibility => IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
    public bool NeedsAttention => InstalledState is "modified" or "missing" or "unreadable"
        || (InstalledState == "installed"
            && InstalledSource == "catalog"
            && !string.IsNullOrWhiteSpace(InstalledVariantId)
            && Variants.All(item => item.VariantId != InstalledVariantId));
    public string RestoreActionText => RequiresForceRestore ? "强制恢复" : "恢复";
    public string InstalledSourceText => IsLocalEdit
        ? "来源：本地编辑"
        : IsLocalImport
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
    public bool CanRequestTranslation => NativeLanguages.Count > 0
        && !HasNativeChinese
        && Variants.Count == 0;
    public Visibility TranslationRequestVisibility => CanRequestTranslation
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool HasCatalogWarning => !IsLocalEdit && !IsLocalImport && !IsCurrent && !HasNativeChinese;
    public string CatalogText => IsLocalEdit
        ? "本地编辑译本"
        : IsLocalImport
            ? "本地导入译本"
        : HasNativeChinese
        ? "本游戏自带中文"
        : $"索引状态：{CatalogStatusPresentation.Label(CatalogStatus)}";
    public string CatalogWarningText => IsLocalEdit || IsLocalImport || HasNativeChinese
        ? string.Empty
        : CatalogStatusPresentation.Warning(CatalogStatus);
    public string Subtitle => $"App ID {AppId}" + (string.IsNullOrWhiteSpace(DiscoveryText) ? string.Empty : $" · {DiscoveryText}");
    public string ContributorText => Contributors.Count == 0
        ? "译本作者：未提供"
        : $"译本作者：{string.Join("、", Contributors)}";
    public string CloudMetadataText => $"App ID {AppId} · {ContributorText}";

    public static GameItem FromPayload(JsonElement payload)
    {
        var item = new GameItem
        {
            AppId = GetString(payload, "app_id", "0"),
            GameName = GetString(payload, "game_name", "未知游戏"),
            CatalogStatus = GetString(payload, "catalog_status", "unknown"),
            Contributors = GetStringArray(payload, "contributors"),
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
                    Sha256 = GetString(variant, "sha256", string.Empty),
                    FileSizeBytes = variant.TryGetProperty("file_size_bytes", out var size)
                        && size.TryGetInt64(out var fileSize)
                            ? fileSize
                            : 0,
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
