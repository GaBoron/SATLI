using System.Text;
using System.Text.Json;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Satli_Gui.Models;
using AchievementTranslation = Satli.Core.Formats.AchievementTranslation;

namespace Satli_Gui.Services;

/// <summary>Unaccepted generated text is separate from manual drafts and scoped to the exact source and language.</summary>
internal sealed class AiProposalStore
{
    private readonly string _path;
    public AiProposalStore(SchemaInspection inspection, string language)
    {
        var identity = $"{(inspection.Project is null ? "bin" : "json")}\n{inspection.AppId}\n{inspection.VariantId}\n{inspection.SourceSha256}\n{language}";
        _path = Path.Combine(LiteWorkflowService.DataDirectory(App.ViewModel.Settings), "ai-proposals",
            FileOperations.Sha256(Encoding.UTF8.GetBytes(identity)) + ".json");
    }

    public async Task<Dictionary<string, AchievementTranslation>> LoadAsync()
    {
        if (!File.Exists(_path)) return new(StringComparer.Ordinal);
        if (new FileInfo(_path).Length > 64 * 1024 * 1024) throw new InvalidDataException("待审结果过大。");
        return JsonSerializer.Deserialize<Dictionary<string, AchievementTranslation>>(await File.ReadAllTextAsync(_path))
            ?? new(StringComparer.Ordinal);
    }

    public Task SaveAsync(IReadOnlyDictionary<string, AchievementTranslation> values) =>
        Task.Run(() => FileOperations.WriteDurable(_path, JsonSerializer.SerializeToUtf8Bytes(values,
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
}
