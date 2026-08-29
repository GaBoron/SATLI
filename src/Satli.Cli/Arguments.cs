using Satli.Core;

namespace Satli.Cli;

internal sealed class Arguments(IReadOnlyList<string> values)
{
    private readonly IReadOnlyList<string> _values = values;
    public bool Has(string option) => _values.Contains(option, StringComparer.Ordinal);
    public string? Value(string option)
    {
        var index = Index(option); if (index < 0) return null;
        if (index + 1 >= _values.Count || _values[index + 1].StartsWith('-')) throw new UsageException($"{option} 缺少参数");
        return _values[index + 1];
    }
    public string Required(string option) => Value(option) ?? throw new UsageException($"缺少 {option}");
    public IReadOnlyList<string> Values(string option)
    {
        var result = new List<string>();
        for (var index = 0; index < _values.Count; index++) if (_values[index] == option)
        {
            if (++index >= _values.Count) throw new UsageException($"{option} 缺少参数"); result.Add(_values[index]);
        }
        return result;
    }
    public IReadOnlyList<string> Positionals(int skip, params string[] valuedOptions)
    {
        var options = valuedOptions.ToHashSet(StringComparer.Ordinal); var result = new List<string>();
        for (var index = skip; index < _values.Count; index++)
        {
            var value = _values[index];
            if (options.Contains(value)) { index++; continue; }
            if (!value.StartsWith('-')) result.Add(value);
        }
        return result;
    }
    public string DataDirectory => Path.GetFullPath(Value("--data-dir")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SATLI"));
    public string? SteamDirectory => Value("--steam-dir");
    private int Index(string option) { for (var i = 0; i < _values.Count; i++) if (_values[i] == option) return i; return -1; }
}
