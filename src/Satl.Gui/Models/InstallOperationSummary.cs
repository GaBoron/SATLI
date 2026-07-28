using System.Text;

namespace Satl_Gui.Models;

public sealed record InstallFailure(string AppId, string GameName, string Message);

public sealed record InstallOperationSummary(
    int Succeeded,
    int Failed,
    IReadOnlyList<InstallFailure> Failures)
{
    public bool HasSucceededItems => Succeeded > 0;

    public string Message
    {
        get
        {
            if (Failed == 0)
            {
                return Succeeded == 1
                    ? "所选翻译已安装。"
                    : $"所选 {Succeeded} 个翻译已安装。";
            }

            var builder = new StringBuilder();
            builder.Append($"批量安装已完成：成功 {Succeeded} 个，失败 {Failed} 个。");
            if (Succeeded > 0)
            {
                builder.Append(" 单项失败未中止后续任务。");
            }
            foreach (var failure in Failures)
            {
                builder.AppendLine();
                builder.Append($"{failure.AppId} {failure.GameName}：{failure.Message}");
            }
            return builder.ToString();
        }
    }

    public static InstallOperationSummary? TryCreate(CliRunResult result)
    {
        var completed = result.Events.LastOrDefault(item =>
            item.Operation == "install" && item.Event == "completed");
        if (completed is null
            || !completed.Payload.TryGetProperty("succeeded", out var succeededValue)
            || !succeededValue.TryGetInt32(out var succeeded)
            || !completed.Payload.TryGetProperty("failed", out var failedValue)
            || !failedValue.TryGetInt32(out var failed))
        {
            return null;
        }

        var failures = result.Events
            .Where(item => item.Operation == "install" && item.Event == "item-failed")
            .Select(item => new InstallFailure(
                StringValue(item, "app_id"),
                StringValue(item, "game_name"),
                StringValue(item, "message")))
            .ToList();
        return new InstallOperationSummary(succeeded, failed, failures);
    }

    private static string StringValue(SatlEvent item, string property) =>
        item.Payload.TryGetProperty(property, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
