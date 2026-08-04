using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed class TranslationUpdateDiffService
{
    public async Task<IReadOnlyList<TranslationUpdateDiff>?> CreateAsync(
        IReadOnlyList<GameItem> selected,
        IReadOnlyList<ReplacementPreview> targetPreviews,
        Func<GameItem, Task<ReplacementPreview?>> currentPreviewLoader)
    {
        var duplicateTarget = targetPreviews
            .GroupBy(preview => preview.AppId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTarget is not null)
        {
            throw new InvalidDataException(
                $"待安装预览包含重复的 App ID：{duplicateTarget.Key}。拒绝生成更新差异。");
        }

        var targetsByAppId = targetPreviews.ToDictionary(
            preview => preview.AppId,
            StringComparer.Ordinal);
        var results = new List<TranslationUpdateDiff>();
        foreach (var game in selected.Where(item => item.IsUpdateAvailable))
        {
            if (!targetsByAppId.TryGetValue(game.AppId, out var target))
            {
                throw new InvalidDataException(
                    $"找不到 App ID {game.AppId} 的待更新内容，拒绝继续安装。");
            }
            if (target.DeletesTarget)
            {
                throw new InvalidDataException(
                    $"App ID {game.AppId} 的更新预览意外要求删除目标文件，拒绝继续安装。");
            }

            var current = await currentPreviewLoader(game);
            if (current is null)
            {
                return null;
            }
            if (!current.AppId.Equals(game.AppId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"当前内容的 App ID 与游戏 {game.AppId} 不一致，拒绝生成更新差异。");
            }

            results.Add(new TranslationUpdateDiff(
                game,
                new SchemaRevisionDiff(current, target)));
        }
        return results;
    }
}
