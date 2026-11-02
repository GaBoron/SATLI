using System.Text;
using System.Text.Json;
using Satli.Core.FileSystem;
using Satli.Core.Formats;

namespace Satli.Core.TranslationProjects;

public sealed class TranslationProjectStore(string directory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SaveAsync(TranslationProject project)
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                Validate(project);
                project.SavedAt = DateTimeOffset.UtcNow;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(project);
                FileOperations.WriteDurable(Path.Combine(directory, $"{project.Id:N}.json"), bytes);
            });
        }
        finally { _gate.Release(); }
    }

    public async Task<(IReadOnlyList<TranslationProject> Projects, int Failed)> LoadAllAsync()
    {
        if (!Directory.Exists(directory)) return ([], 0);
        var projects = new List<TranslationProject>();
        var failed = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new InvalidDataException();
                var project = JsonSerializer.Deserialize<TranslationProject>(await File.ReadAllTextAsync(path, Encoding.UTF8))
                    ?? throw new InvalidDataException();
                Validate(project);
                if (Path.GetFileNameWithoutExtension(path) != project.Id.ToString("N")) throw new InvalidDataException();
                projects.Add(project);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                or JsonException or ArgumentException or SatliException or InvalidOperationException)
            { failed++; }
        }
        return (projects.OrderByDescending(project => project.SavedAt).ToArray(), failed);
    }

    private static void Validate(TranslationProject project)
    {
        if (project.Version != 1 || project.Id == Guid.Empty || project.Edits is null
            || project.Edits.Values.Any(value => value is null || value.Name is null || value.Description is null)
            || project.VariantId is null || project.TargetLanguage is null
            || !uint.TryParse(project.AppId, out var id) || id == 0
            || id.ToString() != project.AppId || !System.Text.RegularExpressions.Regex.IsMatch(project.VariantId, @"\A[a-z0-9][a-z0-9-]{0,63}\z")
            || !System.Text.RegularExpressions.Regex.IsMatch(project.TargetLanguage, @"\A[a-z][a-z0-9_]{1,31}\z"))
            throw new InvalidDataException("翻译工程标识无效。");
        if (project.Original is null) return;
        var bytes = TranslationJsonExport.Serialize(project.Original);
        TranslationJson.Parse(bytes, project.AppId, allowIncomplete: true);
        if (FileOperations.Sha256(bytes) != project.SourceFingerprint
            || project.Original["variant_id"]!.GetValue<string>() != project.VariantId
            || project.Edits.Keys.Any(key => !project.Original["achievements"]!.AsObject().ContainsKey(key)))
            throw new InvalidDataException("翻译工程来源或 API ID 不一致。");
        project.Build(forPublication: false);
    }
}
