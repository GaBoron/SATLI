using Satli_Gui.Models;

namespace Satli_Gui.Services;

public sealed class TranslationCliArguments(
    Func<GuiSettings> settings,
    Func<string?>? detectedSteamDirectory = null)
{
    public List<string> Install(
        IReadOnlyList<GameItem> selected,
        bool dryRun,
        bool yes,
        bool previewContent)
    {
        var arguments = new List<string> { "install" };
        arguments.AddRange(selected.Select(item => item.AppId));
        foreach (var item in selected.Where(item => item.SelectedVariant is not null))
        {
            arguments.AddRange(["--variant", $"{item.AppId}={item.SelectedVariant!.VariantId}"]);
        }
        if (selected.Any(item => !item.IsCurrent))
        {
            arguments.Add("--allow-outdated");
        }
        AppendFlags(arguments, dryRun, yes, previewContent);
        AddCommon(arguments, includeSteamDirectory: true, includeOffline: true);
        return arguments;
    }

    public List<string> Restore(
        IReadOnlyList<GameItem> selected,
        bool dryRun,
        bool yes,
        bool force,
        bool previewContent)
    {
        var arguments = new List<string> { "restore" };
        arguments.AddRange(selected.Select(item => item.AppId));
        if (force)
        {
            arguments.Add("--force");
        }
        AppendFlags(arguments, dryRun, yes, previewContent);
        AddCommon(arguments, includeSteamDirectory: true, includeOffline: false);
        return arguments;
    }

    public List<string> SchemaInspect(string appId)
    {
        var arguments = new List<string> { "schema", "inspect", appId, "--jsonl" };
        AddCommon(arguments, includeSteamDirectory: true, includeOffline: false);
        return arguments;
    }

    public List<string> Protect(IReadOnlyList<GameItem> selected, bool enable)
    {
        var arguments = new List<string> { "protect", enable ? "lock" : "unlock" };
        arguments.AddRange(selected.Select(item => item.AppId));
        if (enable)
        {
            arguments.Add("--yes");
        }
        arguments.Add("--jsonl");
        AddSteamDirectory(arguments);
        return arguments;
    }

    public List<string> CacheRefresh()
    {
        var arguments = new List<string> { "cache", "refresh", "--jsonl" };
        AddDataDirectory(arguments);
        return arguments;
    }

    public List<string> Scan(bool useCatalogCache, out string? warning)
    {
        var arguments = new List<string> { "scan", "--jsonl" };
        AddCommon(arguments, includeSteamDirectory: true, includeOffline: true);
        if (useCatalogCache && !settings().Offline)
        {
            arguments.Add("--catalog-cache-only");
        }
        warning = SteamLibraryCliOptions.AppendScanArguments(arguments, settings());
        return arguments;
    }

    public List<string> Status(bool forceOffline)
    {
        var arguments = new List<string> { "status", "--jsonl" };
        AddCommon(arguments, includeSteamDirectory: false, includeOffline: true, forceOffline);
        return arguments;
    }

    public List<string> PetitionExport(string appId, string outputPath)
    {
        var arguments = new List<string>
        {
            "petition", "export", appId,
            "--output", outputPath,
            "--overwrite",
            "--jsonl",
        };
        AddSteamDirectory(arguments);
        return arguments;
    }

    private static void AppendFlags(
        List<string> arguments,
        bool dryRun,
        bool yes,
        bool previewContent)
    {
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        if (previewContent)
        {
            arguments.Add("--preview-content");
        }
        if (yes)
        {
            arguments.Add("--yes");
        }
        arguments.Add("--jsonl");
    }

    private void AddCommon(
        List<string> arguments,
        bool includeSteamDirectory,
        bool includeOffline,
        bool forceOffline = false)
    {
        AddDataDirectory(arguments);
        if (includeSteamDirectory)
        {
            AddSteamDirectory(arguments);
        }
        if (includeOffline && (settings().Offline || forceOffline))
        {
            arguments.Add("--offline");
        }
    }

    private void AddDataDirectory(List<string> arguments)
    {
        CliConfiguredPaths.AppendDataDirectory(arguments, settings());
    }

    private void AddSteamDirectory(List<string> arguments)
    {
        CliConfiguredPaths.AppendSteamDirectory(
            arguments,
            settings(),
            detectedSteamDirectory?.Invoke());
    }
}
