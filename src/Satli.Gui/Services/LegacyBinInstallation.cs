namespace Satli_Gui.Services;

internal static class LegacyBinInstallation
{
    public const string LockedMessage = "BIN 安装已锁定。如需使用，请在“设置 → Steam 译文显示”开启“使用旧方式安装”。";

    public static bool IsInstallation(IReadOnlyList<string> arguments) => arguments.Count > 0
        && !arguments.Contains("--dry-run", StringComparer.Ordinal)
        && (arguments[0] is "install" or "local-import"
            || arguments[0] == "schema" && arguments.Count > 1
                && (arguments[1] == "apply"
                    || arguments.Count > 2 && arguments[1] == "revisions" && arguments[2] == "activate"));
}
