using System.Collections;
using Satli.Core;
using Satli.Core.State;

namespace Satli.Cli;

/// <summary>
/// Runs SATLI command-line operations without requiring a separate helper process.
/// </summary>
public static class CliApplication
{
    private static readonly object MigrationLock = new();

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        Action<string>? onJsonLine = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var args = arguments.ToArray();
        var jsonLines = args.Contains("--jsonl", StringComparer.Ordinal);
        var events = new EventSink(jsonLines, output, onJsonLine);
        try
        {
            if (!args.Contains("--data-dir", StringComparer.Ordinal)
                && !args.Contains("--help", StringComparer.Ordinal)
                && !args.Contains("-h", StringComparer.Ordinal)
                && !args.Contains("--version", StringComparer.Ordinal))
            {
                lock (MigrationLock)
                {
                    DataDirectoryMigration.MigrateDefault();
                }
            }

            if (args.Contains("--version", StringComparer.Ordinal))
            {
                await output.WriteLineAsync("satli 2.2.2");
                return 0;
            }

            if (args.Length == 0
                || args.Contains("--help", StringComparer.Ordinal)
                || args.Contains("-h", StringComparer.Ordinal))
            {
                await output.WriteLineAsync("SATLI 2.2.2 - 安全安装、管理和恢复 Steam 成就翻译");
                await output.WriteLineAsync("命令：scan, install, local-import, status, restore, protect, cache, petition, schema");
                return args.Length == 0 ? 2 : 0;
            }

            return await new CommandDispatcher(
                events,
                output,
                MergeEnvironment(environmentOverrides)).RunAsync(args);
        }
        catch (SatliException exception)
        {
            var operation = CommandDispatcher.Operation(args);
            if (jsonLines)
            {
                events.Emit(operation, "error", new System.Text.Json.Nodes.JsonObject
                {
                    ["message"] = exception.Message,
                    ["exit_code"] = exception.ExitCode,
                });
            }
            else
            {
                await error.WriteLineAsync($"错误：{exception.Message}");
            }
            return exception.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("已取消。");
            return 2;
        }
        catch (Exception exception)
        {
            if (jsonLines)
            {
                events.Emit(
                    CommandDispatcher.Operation(args),
                    "error",
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["message"] = $"未预期错误：{exception.Message}",
                        ["exit_code"] = 6,
                    });
            }
            else
            {
                await error.WriteLineAsync($"错误：{exception}");
            }
            return 6;
        }
    }

    private static IReadOnlyDictionary<string, string> MergeEnvironment(
        IReadOnlyDictionary<string, string>? overrides)
    {
        var environment = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(
                item => (string)item.Key,
                item => Convert.ToString(item.Value) ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
        if (overrides is null)
        {
            return environment;
        }

        foreach (var item in overrides)
        {
            environment[item.Key] = item.Value;
        }
        return environment;
    }
}
