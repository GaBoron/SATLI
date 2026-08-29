using Satli.Cli;

Console.OutputEncoding = System.Text.Encoding.UTF8;
return await CliApplication.RunAsync(args, Console.Out, Console.Error);
