using System.Diagnostics;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.ViewModels;

public sealed partial class TranslationManagementViewModel
{
    private async Task<CliRunResult> RunCliAsync(IReadOnlyList<string> arguments, string status)
    {
        _operation.SetStatus(status);
        var operation = arguments.FirstOrDefault() ?? "unknown";
        var tracksGameLoading = operation == "scan";
        if (tracksGameLoading)
        {
            _operation.GameLoading.Start(status);
        }
        var stopwatch = Stopwatch.StartNew();
        await App.Logs.WriteAsync("信息", operation, $"开始：{status}");
        await App.Logs.WriteAsync(
            "调试", operation,
            $"GUI 已提交 CLI 操作。状态文本={status}；参数数量={arguments.Count}。",
            debug: true);
        var diagnosticWrites = new List<Task>();
        Action<string>? diagnostic = App.Logs.IsDebugEnabled
            ? message => diagnosticWrites.Add(App.Logs.WriteAsync("调试", operation, message, debug: true))
            : null;
        CliRunResult result;
        try
        {
            var settings = _settings();
            result = await _cli.RunAsync(arguments, satliEvent =>
            {
                _ = App.Logs.WriteAsync(
                    "详细", satliEvent.Operation, CliEventDescription.Format(satliEvent), detailed: true);
                if (tracksGameLoading)
                {
                    void UpdateProgress()
                    {
                        _operation.GameLoading.Handle(satliEvent);
                        if (_operation.GameLoading.IsActive)
                        {
                            _operation.SetStatus(_operation.GameLoading.Text);
                        }
                    }
                    if (App.DispatcherQueue.HasThreadAccess)
                    {
                        UpdateProgress();
                    }
                    else
                    {
                        App.DispatcherQueue.TryEnqueue(UpdateProgress);
                    }
                }
                if (satliEvent.Event == "item-started"
                    && satliEvent.Payload.TryGetProperty("app_id", out var appId))
                {
                    App.DispatcherQueue.TryEnqueue(
                        () => _operation.SetStatus($"正在处理 App ID {appId.GetString()}…"));
                }
                else if (satliEvent.Event == "warning"
                    && satliEvent.Payload.TryGetProperty("message", out var warning))
                {
                    App.DispatcherQueue.TryEnqueue(
                        () => ShowInfo(warning.GetString() ?? "正在使用本地缓存。"));
                }
            }, diagnostic, settings.Network, settings.SteamLibrary, settings.DownloadSources);
        }
        catch (Exception)
        {
            if (tracksGameLoading)
            {
                _operation.GameLoading.Fail("游戏加载失败");
            }
            await Task.WhenAll(diagnosticWrites);
            await App.Logs.WriteAsync(
                "调试", operation,
                $"CLI 调用抛出异常。耗时={stopwatch.ElapsedMilliseconds} ms。",
                debug: true);
            throw;
        }
        await Task.WhenAll(diagnosticWrites);
        if (tracksGameLoading)
        {
            _operation.GameLoading.Finish(result.IsSuccess ? "游戏加载完成" : "游戏加载失败");
        }
        await App.Logs.WriteAsync(
            result.IsSuccess ? "信息" : "错误",
            operation,
            $"完成：退出码 {result.ExitCode}，事件 {result.Events.Count} 个。" +
            (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty : $" 标准错误：{result.StandardError}"));
        await App.Logs.WriteAsync(
            "调试", operation,
            $"CLI 操作返回 GUI。成功={result.IsSuccess}；耗时={stopwatch.ElapsedMilliseconds} ms；" +
            $"错误消息={result.ErrorMessage}。",
            debug: true);
        return result;
    }
}
