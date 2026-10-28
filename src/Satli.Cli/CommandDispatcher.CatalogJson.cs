using System.Text.Json.Nodes;
using Satli.Core;
using Satli.Core.Formats;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private async Task<int> CatalogJson(Arguments args)
    {
        var ids = args.Positionals(1, "--data-dir", "--variant");
        if (ids.Count != 1) throw new UsageException("catalog-json 需要一个 APP_ID");
        var repository = Repository(args);
        var catalog = await repository.LoadAsync(args.Has("--offline"), refresh: !args.Has("--catalog-cache-only"));
        if (!catalog.Entries.TryGetValue(ids[0], out var entry))
            throw new UsageException("翻译库中没有所选 App ID");
        var variantId = args.Value("--variant") ?? "default";
        var variant = entry.Variants.FirstOrDefault(item => item.VariantId == variantId)
            ?? throw new UsageException("翻译库中没有所选 Variant");
        _events.Emit("catalog-json", "item-started", new JsonObject
        {
            ["app_id"] = entry.AppId, ["variant_id"] = variant.VariantId,
        });
        var path = await repository.DownloadJsonAsync(entry.AppId, variant, args.Has("--offline"));
        var bytes = await File.ReadAllBytesAsync(path);
        var translation = TranslationJson.Parse(bytes, entry.AppId, variant);
        var payload = TranslationJson.Preview(translation);
        payload["app_id"] = entry.AppId;
        payload["game_name"] = entry.GameName;
        payload["variant_id"] = variant.VariantId;
        payload["action"] = "json";
        _events.Emit("catalog-json", "item-preview", payload);
        _events.Emit("catalog-json", "completed", new JsonObject
        {
            ["app_id"] = entry.AppId, ["path"] = path,
            ["json_sha256"] = Satli.Core.FileSystem.FileOperations.Sha256(bytes),
            ["json_version"] = variant.Json!.Version, ["exit_code"] = 0,
        });
        return 0;
    }
}
