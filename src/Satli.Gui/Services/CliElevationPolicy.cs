namespace Satli_Gui.Services;

public static class CliElevationPolicy
{
    public static bool RequiresElevation(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments.Contains("--dry-run", StringComparer.Ordinal))
        {
            return false;
        }

        return arguments[0] switch
        {
            "install" or "restore" or "local-import" => true,
            "schema" => RequiresSchemaElevation(arguments),
            _ => false,
        };
    }

    private static bool RequiresSchemaElevation(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return false;
        }

        if (arguments[1] is "apply" or "restore")
        {
            return true;
        }

        return arguments.Count >= 3
            && arguments[1] == "revisions"
            && arguments[2] == "activate";
    }
}
