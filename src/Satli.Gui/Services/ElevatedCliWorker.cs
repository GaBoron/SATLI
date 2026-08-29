using System.IO.Pipes;

namespace Satli_Gui.Services;

internal static class ElevatedCliWorker
{
    public const string ActivationArgument = "--satli-elevated-cli";

    public static bool TryGetPipeName(IReadOnlyList<string> commandLine, out string pipeName)
    {
        pipeName = string.Empty;
        if (commandLine.Count != 3 || commandLine[1] != ActivationArgument)
        {
            return false;
        }

        var candidate = commandLine[2];
        if (!candidate.StartsWith("satli-elevated-", StringComparison.Ordinal)
            || candidate.Length > 80
            || candidate.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            return false;
        }

        pipeName = candidate;
        return true;
    }

    public static async Task RunAsync(string pipeName)
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(30_000);
        var request = await ElevatedCliProtocol.ReadRequestAsync(pipe);
        var diagnostics = new List<string>();
        ElevatedCliResponse response;
        try
        {
            var result = await new CliRuntimeRunner().RunAsync(
                request,
                onDiagnostic: diagnostics.Add);
            response = new ElevatedCliResponse(
                result.ExitCode,
                result.Events.ToList(),
                result.StandardError,
                diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(exception.ToString());
            response = new ElevatedCliResponse(
                -1,
                [],
                $"管理员工作进程执行失败：{exception.Message}",
                diagnostics);
        }
        await ElevatedCliProtocol.WriteResponseAsync(pipe, response);
    }
}
