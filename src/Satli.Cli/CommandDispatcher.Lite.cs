using System.Text.Json.Nodes;
using Satli.Core;

namespace Satli.Cli;

internal sealed partial class CommandDispatcher
{
    private async Task<int> InstallLitePlugin(Arguments args)
    {
        RequireYes(args);
        if (args.Has("--offline")) throw new PreflightException("离线模式下无法下载 SATLI-lite");
        var steam = Satli.Core.Steam.SteamLocator.FindSteamDirectory(args.SteamDirectory);
        using var http = NetworkClient();
        _events.Emit("lite-install", "item-started", new JsonObject { ["message"] = "正在下载官方 SATLI-lite 安装包" });
        await Satli.Core.SatliLite.LitePluginInstaller.InstallAsync(steam, http);
        _events.Emit("lite-install", "completed", new JsonObject
        {
            ["message"] = "插件文件已校验并保存；请在 Steam → Millennium → Plugins 启用并重启 Steam。尚未应用译文。",
            ["exit_code"] = 0,
        });
        return 0;
    }

}
