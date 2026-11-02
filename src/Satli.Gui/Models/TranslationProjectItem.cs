using Satli.Core.TranslationProjects;

namespace Satli_Gui.Models;

public sealed record TranslationProjectItem(TranslationProject Project)
{
    public string Name => Project.GameName;
    public string Identity => $"App ID {Project.AppId} · {Project.VariantId} · {Project.TargetLanguage}";
    public string Status => Project.IsReady ? "可编辑" : "待补充来源";
    public string Saved => $"保存于 {Project.SavedAt.ToLocalTime():g}";
}
