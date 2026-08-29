using System.Globalization;
using Satli.Cli;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

/// <summary>
/// Executes the command core inside the current SATLI process.
/// </summary>
internal sealed class CliRuntimeRunner
{
    public async Task<CliRunResult> RunAsync(
        CliInvocation invocation,
        Action<SatliEvent>? onEvent = null,
        Action<string>? onDiagnostic = null)
    {
        onDiagnostic?.Invoke(
            $"步骤 1：正在同进程运行 SATLI 核心；参数数量={invocation.Arguments.Count}；" +
            $"附加环境变量={string.Join(",", invocation.Environment.Keys)}。");

        var events = new List<SatliEvent>();
        using var standardError = new StringWriter(CultureInfo.InvariantCulture);
        var outputLine = 0;
        var exitCode = await CliApplication.RunAsync(
            invocation.Arguments,
            TextWriter.Null,
            standardError,
            invocation.Environment,
            line =>
            {
                outputLine++;
                onDiagnostic?.Invoke($"步骤 2.{outputLine}：收到核心事件：{line}");
                var parsed = CliEventParser.Parse(line);
                events.Add(parsed);
                onEvent?.Invoke(parsed);
            });

        var error = standardError.ToString().Trim();
        onDiagnostic?.Invoke(
            $"步骤 3：同进程核心执行结束；退出码={exitCode}；事件数={events.Count}；" +
            $"标准错误={(string.IsNullOrEmpty(error) ? "<空>" : error)}。");
        return new CliRunResult(exitCode, events, error);
    }
}
