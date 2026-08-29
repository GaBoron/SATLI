using Satli.Core;
using Satli.Core.State;

namespace Satli.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            if (!args.Contains("--data-dir")
                && !args.Contains("--help")
                && !args.Contains("-h")
                && !args.Contains("--version"))
                DataDirectoryMigration.MigrateDefault();
            if (args.Contains("--version")) { Console.WriteLine("satli 2.1.0"); return 0; }
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("SATLI 2.1.0 - 安全安装、管理和恢复 Steam 成就翻译");
                Console.WriteLine("命令：scan, install, local-import, status, restore, protect, cache, petition, schema");
                return args.Length == 0 ? 2 : 0;
            }
            return await new CommandDispatcher(new EventSink(args.Contains("--jsonl"))).RunAsync(args);
        }
        catch (SatliException exception)
        {
            var operation = CommandDispatcher.Operation(args);
            if (args.Contains("--jsonl")) new EventSink(true).Emit(operation, "error",
                new System.Text.Json.Nodes.JsonObject { ["message"] = exception.Message, ["exit_code"] = exception.ExitCode });
            else Console.Error.WriteLine($"错误：{exception.Message}");
            return exception.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("已取消。"); return 2;
        }
        catch (Exception exception)
        {
            if (args.Contains("--jsonl")) new EventSink(true).Emit(CommandDispatcher.Operation(args), "error",
                new System.Text.Json.Nodes.JsonObject { ["message"] = $"未预期错误：{exception.Message}", ["exit_code"] = 6 });
            else Console.Error.WriteLine($"错误：{exception}");
            return 6;
        }
    }
}
