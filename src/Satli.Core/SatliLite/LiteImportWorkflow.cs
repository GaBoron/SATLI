using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Satli.Core.SatliLite;

/// <summary>Resumes the selected translation after setup, retaining one query or import request across reconnects.</summary>
public sealed class LiteImportWorkflow(LiteClient client)
{
    public async Task<LiteResult?> ApplyAsync(string appId, JsonObject translation, string language, string sourceKind,
        Func<Task<bool>> ensureConnected, Func<JsonObject, Task<bool>> confirmReplacement,
        string? sourceVersion = null, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await ensureConnected()) return null;
            try
            {
                var previous = await client.ResumeAsync(appId, TimeSpan.Zero, cancellationToken);
                if (previous?.Pending == true && previous.Operation == "import") return previous;

                progress?.Invoke("已连接，正在查询当前译本…");
                var query = previous?.Pending == true && previous.Operation == "query_app"
                    ? previous : await client.QueryAsync(appId, cancellationToken, requireConnected: true);
                if (query.Operation == "import") return query;
                var timer = Stopwatch.StartNew();
                while (query.Pending)
                {
                    if (!(await client.InspectAsync(cancellationToken)).Connected)
                        throw new LiteConnectionUnavailableException();
                    if (timer.Elapsed >= TimeSpan.FromSeconds(30))
                        throw new PreflightException("lite 状态查询尚未完成，未提交新的译文；原请求已保留，请稍后重试。");
                    progress?.Invoke("正在等待 lite 返回状态，完成后继续安装已选译文…");
                    query = await client.ResumeAsync(appId, TimeSpan.FromSeconds(1), cancellationToken)
                        ?? throw new IntegrityException("lite 状态查询记录丢失，未提交译文。");
                    if (query.Operation == "import") return query;
                }
                if (!query.Succeeded) return query;
                if (!(await client.InspectAsync(cancellationToken)).Connected)
                    throw new LiteConnectionUnavailableException();

                JsonObject? conflict = null;
                if (query.App?["exists"]?.GetValue<bool>() == true)
                {
                    if (!await confirmReplacement(query.App)) return null;
                    conflict = new JsonObject
                    {
                        ["mode"] = "replace", ["expected_revision"] = query.App["revision"]!.DeepClone(),
                        ["allow_local_edits"] = query.App["local_edits"]?.GetValue<bool>() == true,
                    };
                }
                progress?.Invoke("正在向 lite 安装已选译文…");
                return await Task.Run(() => client.ImportAsync(appId, translation, language, sourceKind, conflict,
                    cancellationToken, sourceVersion, requireConnected: true), cancellationToken);
            }
            catch (LiteConnectionUnavailableException)
            {
                progress?.Invoke("lite 连接中断，已保留当前选择；连接后继续。");
            }
        }
    }
}
