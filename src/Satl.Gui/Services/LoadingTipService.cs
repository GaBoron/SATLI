using System.Diagnostics;
using System.Text;

namespace Satl_Gui.Services;

internal sealed class LoadingTipService
{
    private const string FileName = "loading-tips.txt";
    private readonly Func<int, int> _selectIndex;

    internal static IReadOnlyList<string> DefaultTips { get; } =
    [
        "正在给每个成就贴上中文翻译。",
        "Steam 在数游戏，我们在数成就。",
        "别急，翻译正在从云端坐电梯下来。",
        "正在确认这不是“获得全部成就”本身。",
        "缓存正在热身，马上进入状态。",
        "正在和古老的 BIN 文件友好握手。",
        "有些成就藏得很深，我们带了手电筒。",
        "正在把 Achievement Unlocked 变得更亲切一点。",
        "扫描期间适合伸个懒腰，但别走太远。",
        "加载环如果会说话，它现在大概在说：快了快了。",
        "正在检查每一枚像素有没有好好上班。",
        "云端目录正在翻书，请勿催页。",
        "有没有一种可能，显示什么 Tip 是可以更改的。",
        "Ciallo～(∠・ω< )⌒★",
        "如果成就无法加载或者过时，可以去报告错误。",
        "这是一条 Tip 。",
        "这不是一条 Tip 。",
        "等等，这条 Tip 加载过几次了？",
        "如果你喜欢这个软件，可以去爱发电赞助作者呦。",
        "你也想贡献自己的翻译吗？随时欢迎！",
        "喜欢唱、跳、Rap ...",
        "有 Bug 怎么办？设置里面有反馈渠道哦。",
        "汪！汪汪！汪汪汪！",
        "喵！喵喵！喵喵喵！",
        "一生短暂，要用力给人间留下印象。",
        "关注 GaBoron 喵，关注 GaBoron 谢谢喵~",
        "一、二、三，跳！跳进...",
        "X X XXX",
        "这些沙雕 Tip 到底是谁写的（心虚）",
        "有问题可以给 SATLI.support@proton.me 发邮件。",
        "Tip: Tip: Tip: Tip: Tip: Null",
        "翻译，轻而易举啊！坏了坏了...",
        "听说 Loading 改成 Thinking 可以让程序看起来更高级。",
        "去 GitHub 看看作者的其他项目吧。",
        "程序正在攻击你的硬盘！等等别卸载！开玩笑的。",
        "听说 macOS 不能打游戏，那我要开发 macOS 版吗？",
        "冷知识：这是一条热知识。",
        "你说的对，但是 SATLI 是由 GaBoron 自主研发的一款更改 Steam 成就显示文本的管理软件。",
        "sk-kfccrazythursdayvme50",
    ];

    public LoadingTipService(string dataDirectory, Func<int, int>? selectIndex = null)
    {
        FilePath = Path.Combine(dataDirectory, FileName);
        _selectIndex = selectIndex ?? Random.Shared.Next;
    }

    public string FilePath { get; }

    public async Task<string> GetTipAsync()
    {
        var tips = await LoadOrCreateAsync();
        var index = Math.Clamp(_selectIndex(tips.Count), 0, tips.Count - 1);
        return tips[index];
    }

    internal async Task<IReadOnlyList<string>> LoadOrCreateAsync()
    {
        await EnsureFileAsync();
        var lines = await File.ReadAllLinesAsync(FilePath, Encoding.UTF8);
        var tips = lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        return tips.Length == 0 ? DefaultTips : tips;
    }

    public async Task<bool> OpenForEditingAsync()
    {
        try
        {
            await EnsureFileAsync();
            Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureFileAsync()
    {
        if (File.Exists(FilePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var lines = new[]
        {
            "# SATL 加载提示：每行一条，空行和以 # 开头的行会被忽略。",
            "# 保存后会在下次扫描时随机显示；应用不会覆盖你的修改。",
            "# 小彩蛋：双击应用加载页里的 Tip，可以再次打开这个文件。",
            string.Empty,
        }.Concat(DefaultTips);
        try
        {
            await File.WriteAllLinesAsync(FilePath, lines, Encoding.UTF8);
        }
        catch (IOException) when (File.Exists(FilePath))
        {
            // Another startup path created the same file first.
        }
    }
}
