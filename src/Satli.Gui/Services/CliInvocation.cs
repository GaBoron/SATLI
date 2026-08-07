namespace Satli_Gui.Services;

internal sealed record CliInvocation(
    List<string> Arguments,
    Dictionary<string, string> Environment);

internal sealed record ElevatedCliResponse(
    int ExitCode,
    List<Models.SatliEvent> Events,
    string StandardError,
    List<string> Diagnostics);
