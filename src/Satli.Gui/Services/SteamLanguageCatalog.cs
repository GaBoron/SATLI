namespace Satli_Gui.Services;

internal sealed record SteamLanguageOption(string Code, string DisplayName);

internal static class SteamLanguageCatalog
{
    private static readonly IReadOnlyList<SteamLanguageOption> Presets =
    [
        Option("schinese", "简体中文"),
        Option("tchinese", "繁体中文"),
        Option("english", "英语"),
        Option("japanese", "日语"),
        Option("koreana", "韩语"),
        Option("arabic", "阿拉伯语"),
        Option("bulgarian", "保加利亚语"),
        Option("czech", "捷克语"),
        Option("danish", "丹麦语"),
        Option("dutch", "荷兰语"),
        Option("finnish", "芬兰语"),
        Option("french", "法语"),
        Option("german", "德语"),
        Option("greek", "希腊语"),
        Option("hungarian", "匈牙利语"),
        Option("indonesian", "印度尼西亚语"),
        Option("italian", "意大利语"),
        Option("malay", "马来语"),
        Option("norwegian", "挪威语"),
        Option("polish", "波兰语"),
        Option("portuguese", "葡萄牙语"),
        Option("brazilian", "巴西葡萄牙语"),
        Option("romanian", "罗马尼亚语"),
        Option("russian", "俄语"),
        Option("spanish", "西班牙语"),
        Option("latam", "拉丁美洲西班牙语"),
        Option("swedish", "瑞典语"),
        Option("thai", "泰语"),
        Option("turkish", "土耳其语"),
        Option("ukrainian", "乌克兰语"),
        Option("vietnamese", "越南语"),
    ];

    private static readonly IReadOnlyDictionary<string, SteamLanguageOption> ByCode =
        Presets.ToDictionary(option => option.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SteamLanguageOption> CreateEditorOptions(
        IEnumerable<string> existingLanguages)
    {
        var options = Presets.ToList();
        var codes = new HashSet<string>(
            options.Select(option => option.Code),
            StringComparer.OrdinalIgnoreCase);
        foreach (var value in existingLanguages)
        {
            var code = value.Trim().ToLowerInvariant();
            if (code.Length > 0 && codes.Add(code))
            {
                options.Add(CreateOption(code));
            }
        }
        return options;
    }

    public static SteamLanguageOption CreateOption(string code)
    {
        var normalized = code.Trim().ToLowerInvariant();
        return ByCode.TryGetValue(normalized, out var option)
            ? option
            : new SteamLanguageOption(normalized, normalized);
    }

    private static SteamLanguageOption Option(string code, string displayName) =>
        new(code, $"{displayName} ({code})");
}
