using Microsoft.UI.Dispatching;

namespace Satl_Gui.Services;

public static class DispatcherQueueExtensions
{
    public static Task<T> EnqueueAsync<T>(
        this DispatcherQueue dispatcher,
        Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    completion.TrySetResult(await action());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            completion.TrySetException(
                new InvalidOperationException("无法将文件选择器调度到界面线程。"));
        }
        return completion.Task;
    }
}
