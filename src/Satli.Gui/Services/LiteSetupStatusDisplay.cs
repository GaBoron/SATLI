using Satli.Core.SatliLite;

namespace Satli_Gui.Services;

/// <summary>Keeps installation, persisted activation and live connection distinct in setup guidance.</summary>
internal sealed record LiteSetupStatusDisplay(string Millennium, string Installed, string Enabled, string Connection, string NextStep)
{
    public static LiteSetupStatusDisplay From(LiteSetupStatus status)
    {
        var connection = status.Connection;
        var enabled = !status.PluginInstalled ? "待安装"
            : !status.MillenniumInstalled ? "等待 Millennium"
            : status.PluginEnabled switch { true => "已启用", false => "未启用", null => "尚未确认" };
        var runtime = connection.Connected ? "已连接"
            : connection.State == "steam_stopped" ? "Steam 未运行"
            : status.PluginEnabled == false ? "等待启用"
            : connection.State == "incompatible" ? "需要更新"
            : connection.State == "legacy_bridge" ? "旧桥待迁移"
            : "未连接";
        return new(status.MillenniumInstalled ? "已安装" : "未安装", status.PluginInstalled ? "已安装" : "未安装",
            enabled, runtime, NextStepFor(status));
    }

    private static string NextStepFor(LiteSetupStatus status)
    {
        var connection = status.Connection;
        if (connection.State == "legacy_bridge")
            return "请先移除旧显示桥，再启用 SATLI-lite；原有快照和备份会保留。";
        if (!status.MillenniumInstalled)
            return "点击安装，由 SATLI 一并下载安装 Millennium 和 lite。";
        if (!status.PluginInstalled)
            return "Millennium 已安装，点击安装 SATLI-lite。";
        if (status.PluginEnabled == false)
            return "在 Steam → Millennium → Plugins 中启用 SATLI lite，然后刷新状态；仍未连接时重启 Steam。";
        if (connection.State == "steam_stopped")
            return "启动 Steam 后刷新状态。";
        if (connection.State == "incompatible")
            return "请在 Millennium 中更新 SATLI-lite，然后重启 Steam 并刷新状态。";
        if (connection.Connected)
            return "已就绪。可发送译文，或在 Steam 的 SATLI lite 设置中管理译本、语言和显示开关。";
        if (status.PluginEnabled == true)
            return "lite 已启用，但尚未连接。请完全退出并重启 Steam，然后刷新状态。";
        return "尚未确认启用状态。请在 Steam → Millennium → Plugins 检查 SATLI lite 开关，然后刷新状态。";
    }
}
