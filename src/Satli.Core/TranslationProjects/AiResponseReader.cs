using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Satli.Core.TranslationProjects;

public sealed record AiStreamActivity(string Stage, string SummaryDelta = "", int ReceivedCharacters = 0);
public sealed record AiReference(string Title, string Url)
{
    public static AiReference? Create(string? title, string? url)
    {
        if (url is null || url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0) return null;
        return new(string.IsNullOrWhiteSpace(title) ? uri.Host : title[..Math.Min(title.Length, 256)], uri.AbsoluteUri);
    }
}

/// <summary>Accumulates provider SSE events while exposing only public progress and reasoning summaries.</summary>
internal static class AiResponseReader
{
    public static async Task<JsonObject> ReadAsync(HttpResponseMessage response, string protocol,
        Action<AiStreamActivity>? activity, CancellationToken token)
    {
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
        {
            activity?.Invoke(new("等待服务返回完整结果（此接口未使用流式响应）"));
            var completeRoot = JsonNode.Parse(await SteamPublicAchievements.ReadLimitedAsync(response, 4 * 1024 * 1024, token))!.AsObject();
            EmitCompletedReasoning(completeRoot, protocol, activity);
            return completeRoot;
        }
        var answer = new StringBuilder();
        var blocks = new SortedDictionary<int, JsonObject>();
        var inputs = new Dictionary<int, StringBuilder>();
        var metadata = new JsonArray();
        JsonObject? completed = null;
        var finished = false;
        var stopReason = "";
        var reasoningReceived = false;
        await foreach (var data in EventsAsync(response, token))
        {
            if (data == "[DONE]") break;
            var value = JsonNode.Parse(data)!.AsObject();
            var type = value["type"]?.ToString() ?? "";
            if (type == "error" || type == "response.failed" || type == "response.incomplete")
                throw new IntegrityException("服务中断生成或未完成输出。");
            if (protocol == "responses")
            {
                if (type == "response.completed")
                {
                    completed = value["response"]!.DeepClone().AsObject();
                    if (!reasoningReceived) EmitCompletedReasoning(completed, protocol, activity);
                    finished = true;
                }
                else if (type is "response.created" or "response.in_progress")
                    activity?.Invoke(new("服务已接收，正在处理"));
                else if (type == "response.output_text.delta")
                {
                    answer.Append(value["delta"]!.GetValue<string>());
                    activity?.Invoke(new("正在生成译文", ReceivedCharacters: answer.Length));
                }
                else if (type is "response.reasoning_summary_text.delta" or "response.reasoning_text.delta")
                {
                    reasoningReceived = true;
                    activity?.Invoke(new(type == "response.reasoning_text.delta" ? "正在思考（服务提供的文本）" : "正在思考（服务提供的摘要）",
                        value["delta"]!.GetValue<string>(), answer.Length));
                }
                else if (type.StartsWith("response.web_search_call.", StringComparison.Ordinal))
                    activity?.Invoke(new(type.EndsWith("completed", StringComparison.Ordinal) ? "资料查询完成" : "正在查询资料", ReceivedCharacters: answer.Length));
                else if (type is "response.output_item.done" or "response.output_text.annotation.added") metadata.Add(value.DeepClone());
            }
            else if (protocol == "chat")
            {
                var choice = value["choices"]?.AsArray().FirstOrDefault();
                if (choice?["finish_reason"]?.ToString() is { Length: > 0 } reason)
                {
                    if (reason != "stop") throw new IntegrityException("服务未完整返回译文。");
                    finished = true;
                }
                var delta = choice?["delta"];
                if (delta?["content"] is JsonValue text)
                {
                    answer.Append(text.GetValue<string>());
                    activity?.Invoke(new("正在生成译文", ReceivedCharacters: answer.Length));
                }
                if (delta?["reasoning_content"] is JsonValue reasoning)
                    activity?.Invoke(new("正在思考（服务提供的文本）", reasoning.GetValue<string>(), answer.Length));
                if (delta?["annotations"] is JsonArray annotations) metadata.Add(annotations.DeepClone());
            }
            else
            {
                var index = value["index"]?.GetValue<int>() ?? 0;
                if (type == "content_block_start")
                {
                    var block = value["content_block"]!.DeepClone().AsObject();
                    blocks[index] = block;
                    if (block["type"]?.ToString() is "tool_use" or "server_tool_use") inputs[index] = new StringBuilder();
                    if (block["type"]?.ToString() == "server_tool_use") activity?.Invoke(new("正在查询资料"));
                    if (block["type"]?.ToString() == "web_search_tool_result") activity?.Invoke(new("收到资料查询结果"));
                }
                else if (type == "content_block_delta")
                {
                    var delta = value["delta"]!;
                    switch (delta["type"]?.ToString())
                    {
                        case "text_delta":
                            var text = delta["text"]!.GetValue<string>();
                            blocks[index]["text"] = (blocks[index]["text"]?.ToString() ?? "") + text;
                            answer.Append(text);
                            activity?.Invoke(new("正在生成译文", ReceivedCharacters: answer.Length));
                            break;
                        case "input_json_delta":
                            inputs[index].Append(delta["partial_json"]!.GetValue<string>());
                            activity?.Invoke(new("正在生成译文", ReceivedCharacters: inputs.Values.Sum(input => input.Length)));
                            break;
                        case "thinking_delta":
                            activity?.Invoke(new("正在思考（服务提供的文本）", delta["thinking"]!.GetValue<string>(), answer.Length));
                            break;
                        case "citations_delta": metadata.Add(delta["citation"]!.DeepClone()); break;
                    }
                }
                else if (type == "message_delta") stopReason = value["delta"]?["stop_reason"]?.ToString() ?? "";
                else if (type == "message_stop") finished = true;
                else if (type == "ping") activity?.Invoke(new("服务连接正常，等待生成结果", ReceivedCharacters: answer.Length));
            }
            if (answer.Length > 4 * 1024 * 1024 || inputs.Values.Sum(input => input.Length) > 4 * 1024 * 1024)
                throw new IntegrityException("AI 回复超过大小限制。");
            if (finished) break;
        }
        if (!finished) throw new IntegrityException("流式响应提前结束，未接受不完整译文。");
        JsonObject root;
        if (protocol == "responses") root = completed ?? throw new IntegrityException("缺少完整响应确认。");
        else if (protocol == "chat") root = new JsonObject { ["choices"] = new JsonArray(new JsonObject
            { ["message"] = new JsonObject { ["content"] = answer.ToString() } }) };
        else
        {
            foreach (var pair in inputs)
                if (pair.Value.Length > 0) blocks[pair.Key]["input"] = JsonNode.Parse(pair.Value.ToString());
            root = new JsonObject { ["content"] = new JsonArray(blocks.Values.Select(block => (JsonNode)block).ToArray()), ["stop_reason"] = stopReason };
        }
        root["stream_metadata"] = metadata;
        activity?.Invoke(new("正在校验生成结果", ReceivedCharacters: answer.Length));
        return root;
    }

    private static void EmitCompletedReasoning(JsonObject root, string protocol, Action<AiStreamActivity>? activity)
    {
        if (activity is null) return;
        void Emit(string? text)
        {
            if (!string.IsNullOrEmpty(text)) activity(new("收到服务提供的思考文本或摘要", text));
        }
        if (protocol == "chat") Emit(root["choices"]?.AsArray().FirstOrDefault()?["message"]?["reasoning_content"]?.GetValue<string>());
        else if (protocol == "anthropic")
        {
            foreach (var block in root["content"]?.AsArray() ?? [])
                if (block?["type"]?.ToString() == "thinking") Emit(block["thinking"]?.GetValue<string>());
        }
        else
        {
            foreach (var item in root["output"]?.AsArray() ?? [])
            {
                if (item?["type"]?.ToString() != "reasoning") continue;
                foreach (var part in (item["content"]?.AsArray() ?? []).Concat(item["summary"]?.AsArray() ?? []))
                    if (part?["type"]?.ToString() is "reasoning_text" or "summary_text") Emit(part["text"]?.GetValue<string>());
            }
        }
    }

    public static IReadOnlyList<AiReference> References(JsonNode root)
    {
        var references = new Dictionary<string, AiReference>(StringComparer.Ordinal);
        void Visit(JsonNode? node)
        {
            if (references.Count >= 100) return;
            if (node is JsonObject map)
            {
                if (map["url"] is JsonValue url && url.TryGetValue<string>(out var address))
                {
                    var title = map["title"] is JsonValue name && name.TryGetValue<string>(out var text) ? text : null;
                    var reference = AiReference.Create(title, address);
                    if (reference is not null) references.TryAdd(reference.Url, reference);
                }
                foreach (var pair in map) Visit(pair.Value);
            }
            else if (node is JsonArray array) foreach (var item in array) Visit(item);
        }
        Visit(root);
        return references.Values.ToArray();
    }

    private static async IAsyncEnumerable<string> EventsAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var buffer = new char[4096];
        var line = new StringBuilder();
        var data = new StringBuilder();
        var received = 0;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) break;
            received += count;
            if (received > 8 * 1024 * 1024) throw new IntegrityException("流式响应超过大小限制。");
            for (var index = 0; index < count; index++)
            {
                var value = buffer[index];
                if (value != '\n')
                {
                    line.Append(value);
                    if (line.Length > 1024 * 1024) throw new IntegrityException("流式事件超过大小限制。");
                    continue;
                }
                var text = line.ToString().TrimEnd('\r');
                line.Clear();
                if (text.Length == 0 && data.Length > 0)
                {
                    yield return data.ToString().TrimEnd('\n');
                    data.Clear();
                }
                else if (text.StartsWith("data:", StringComparison.Ordinal)) data.Append(text[5..].TrimStart(' ')).Append('\n');
            }
        }
        if (line.ToString().StartsWith("data:", StringComparison.Ordinal)) data.Append(line.ToString()[5..].TrimStart(' '));
        if (data.Length > 0) yield return data.ToString().TrimEnd('\n');
    }
}
