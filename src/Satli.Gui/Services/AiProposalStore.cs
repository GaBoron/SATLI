using System.Text;
using System.Text.Json;
using Satli.Core.FileSystem;
using Satli.Core.Formats;
using Satli.Core.TranslationProjects;
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

    public async Task<IReadOnlyList<AiReference>> LoadReferencesAsync()
    {
        var path = Path.ChangeExtension(_path, ".sources.json");
        if (!File.Exists(path)) return [];
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException();
            var values = JsonSerializer.Deserialize<AiReference[]>(await File.ReadAllTextAsync(path)) ?? [];
            return CleanReferences(values);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            await App.Logs.WriteAsync("警告", "AI 翻译", "待审参考来源无法读取，已忽略；译文仍可审阅。");
            return [];
        }
    }

    public Task SaveAsync(IReadOnlyDictionary<string, AchievementTranslation> values,
        IReadOnlyList<AiReference>? references = null) => Task.Run(() =>
    {
        FileOperations.WriteDurable(_path, JsonSerializer.SerializeToUtf8Bytes(values,
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        if (references is not null)
            FileOperations.WriteDurable(Path.ChangeExtension(_path, ".sources.json"),
                JsonSerializer.SerializeToUtf8Bytes(CleanReferences(references)));
    });

    private static AiReference[] CleanReferences(IEnumerable<AiReference> values) => values
        .OfType<AiReference>().Select(value => AiReference.Create(value.Title, value.Url)).OfType<AiReference>()
        .DistinctBy(value => value.Url, StringComparer.Ordinal).Take(100).ToArray();
}
