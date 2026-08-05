using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

internal sealed class ElevatedCliRunner
{
    public async Task<CliRunResult> RunAsync(
        CliInvocation invocation,
        Action<SatlEvent>? onEvent,
        Action<string>? onDiagnostic)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("无法确定管理员工作进程的程序路径。");
        }

        var pipeName = $"satl-elevated-{Guid.NewGuid():N}";
        using var pipe = CreatePipe(
            pipeName,
            WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("无法确定当前 Windows 用户。"));
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
        };
        startInfo.ArgumentList.Add(ElevatedCliWorker.ActivationArgument);
        startInfo.ArgumentList.Add(pipeName);

        onDiagnostic?.Invoke("该操作需要写入 Steam 文件，正在请求管理员权限。");
        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动管理员工作进程。");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("已取消管理员权限请求。", exception);
        }

        using (process)
        {
            await WaitForConnectionAsync(pipe, process);
            await ElevatedCliProtocol.WriteRequestAsync(pipe, invocation);
            ElevatedCliResponse response;
            try
            {
                response = await ElevatedCliProtocol.ReadResponseAsync(pipe);
            }
            catch (EndOfStreamException exception)
            {
                await process.WaitForExitAsync();
                throw new InvalidOperationException(
                    $"管理员工作进程未返回结果（退出码 {process.ExitCode}）。",
                    exception);
            }

            await process.WaitForExitAsync();
            foreach (var diagnostic in response.Diagnostics)
            {
                onDiagnostic?.Invoke($"管理员工作进程：{diagnostic}");
            }
            foreach (var satlEvent in response.Events)
            {
                onEvent?.Invoke(satlEvent);
            }
            return new CliRunResult(response.ExitCode, response.Events, response.StandardError);
        }
    }

    public static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static NamedPipeServerStream CreatePipe(
        string pipeName,
        SecurityIdentifier currentUser)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private static async Task WaitForConnectionAsync(
        NamedPipeServerStream pipe,
        Process process)
    {
        var connection = pipe.WaitForConnectionAsync();
        var exit = process.WaitForExitAsync();
        var completed = await Task.WhenAny(connection, exit);
        if (completed == exit && !pipe.IsConnected)
        {
            throw new InvalidOperationException(
                $"管理员工作进程在建立安全连接前退出（退出码 {process.ExitCode}）。");
        }
        await connection;
    }
}
