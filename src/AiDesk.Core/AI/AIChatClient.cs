using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiDesk.Core.AI;

/// <summary>AI 对话配置（OpenAI 兼容 API）。</summary>
public sealed class AIChatSettings
{
    /// <summary>API 端点（如 https://api.openai.com/v1 或 DeepSeek/智谱等兼容端点）。</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>API Key。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>模型名（如 gpt-4o-mini / deepseek-chat）。</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>系统提示词（宠物人格）。</summary>
    public string SystemPrompt { get; set; } =
        "你是一个友好的桌面宠物 AI 助手。回复使用纯文字，简洁友好，不使用任何表情符号、emoji、颜文字或图标。你可以使用工具帮用户处理电脑问题（启动应用、查询系统信息）。";

    /// <summary>TTS 音色（edge:音色名 或 sapi:zh-CN，见 PetTtsService.Voices）。</summary>
    public string Voice { get; set; } = "edge:zh-CN-XiaoxiaoNeural";

    /// <summary>工具权限规则：工具名 → allow / deny / ask（缺省 ask）。</summary>
    public Dictionary<string, string> ToolPermissions { get; set; } = new();
}

/// <summary>AI 对话回复。</summary>
public sealed record AIChatReply
{
    public string? Content { get; init; }
    public string? Error { get; init; }
    public bool IsError => Error is not null;
}

/// <summary>AI 工具定义（OpenAI function calling）。</summary>
public sealed record AITool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersJsonSchema { get; init; }

    /// <summary>危险操作：执行前需用户确认。</summary>
    public bool RequireConfirm { get; init; }
}

/// <summary>
/// OpenAI 兼容 Chat Completions 客户端（DeepSeek/智谱/OpenAI 等均适用）。
/// 支持工具调用（function calling）、SSE 流式输出、失败重试、工具输出截断与 Doom-loop 检测。
/// </summary>
public sealed class AIChatClient : IDisposable
{
    private readonly HttpClient _http;
    private const int MaxToolRounds = 4;
    private const int ToolOutputMaxChars = 2000;
    private const int DoomLoopThreshold = 3;

    public AIChatClient() : this(new HttpClientHandler())
    {
    }

    /// <summary>注入自定义 handler（测试用）。</summary>
    public AIChatClient(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>发送单轮对话，返回回复内容。失败返回带 Error 的回复。</summary>
    public async Task<AIChatReply> ChatAsync(AIChatSettings settings, string userMessage,
        IReadOnlyList<(string Role, string Content)>? history = null, CancellationToken ct = default)
    {
        if (settings is null)
            return new AIChatReply { Error = "AI 配置为空" };
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return new AIChatReply { Error = "未配置 API Key，请在设置中填写" };
        if (string.IsNullOrWhiteSpace(userMessage))
            return new AIChatReply { Error = "消息为空" };

        try
        {
            var messages = new List<object>();
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
                messages.Add(new { role = "system", content = settings.SystemPrompt });
            if (history is not null)
                foreach (var (role, content) in history)
                    messages.Add(new { role, content });
            messages.Add(new { role = "user", content = userMessage });

            var payload = new
            {
                model = settings.Model,
                messages,
                max_tokens = 500,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{settings.BaseUrl.TrimEnd('/')}/chat/completions");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {settings.ApiKey}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new AIChatReply
                {
                    Error = $"API 错误 {(int)response.StatusCode}: {Truncate(body, 200)}",
                };
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var reply = root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                ? choices[0].TryGetProperty("message", out var msg)
                    ? msg.TryGetProperty("content", out var c) ? c.GetString() : null
                    : null
                : null;
            return new AIChatReply { Content = reply ?? string.Empty };
        }
        catch (OperationCanceledException)
        {
            return new AIChatReply { Error = "请求超时" };
        }
        catch (Exception ex)
        {
            return new AIChatReply { Error = $"请求失败: {ex.Message}" };
        }
    }

    /// <summary>
    /// 带工具调用（function calling）的对话：模型请求工具 → executor 执行 → 结果回传，循环直到最终回复。
    /// onDelta：流式增量文本（启用后走 SSE 流式）；onToolRunning：每个工具执行前回调（UI 进度反馈）。
    /// </summary>
    public async Task<AIChatReply> ChatWithToolsAsync(AIChatSettings settings, string userMessage,
        IReadOnlyList<AITool> tools, Func<string, string, Task<string>> executor,
        IReadOnlyList<(string Role, string Content)>? history = null, CancellationToken ct = default,
        Action<string>? onDelta = null, Action<string, string>? onToolRunning = null)
    {
        if (settings is null)
            return new AIChatReply { Error = "AI 配置为空" };
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return new AIChatReply { Error = "未配置 API Key，请在设置中填写" };
        if (string.IsNullOrWhiteSpace(userMessage))
            return new AIChatReply { Error = "消息为空" };

        try
        {
            var messages = new JsonArray();
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
                messages.Add(new JsonObject { ["role"] = "system", ["content"] = settings.SystemPrompt });
            if (history is not null)
                foreach (var (role, content) in history)
                    messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = userMessage });

            var toolsJson = new JsonArray();
            foreach (var tool in tools)
            {
                toolsJson.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema),
                    },
                });
            }

            var payload = new JsonObject
            {
                ["model"] = settings.Model,
                ["messages"] = messages,
                ["max_tokens"] = 800,
            };
            if (tools.Count > 0)
                payload["tools"] = toolsJson;

            // Doom-loop 检测：连续相同工具+参数调用
            var recentCalls = new List<(string Name, string Args)>();
            var loopStopped = false;

            for (var round = 0; round < MaxToolRounds; round++)
            {
                var reply = onDelta is null
                    ? await SendOnceAsync(settings, payload, ct)
                    : await SendOnceStreamingAsync(settings, payload, ct, onDelta);
                if (reply.Error is not null)
                    return new AIChatReply { Error = reply.Error };
                if (reply.ToolCallsRequested)
                {
                    // 协议要求：先回传带 tool_calls 的 assistant 消息，再回传各 tool 结果
                    if (reply.AssistantMessage is not null)
                        messages.Add(reply.AssistantMessage);
                    foreach (var call in reply.ToolCalls!)
                    {
                        // Doom-loop：同工具同参数连续 3 次 → 拦截并提示模型换策略（持续拦截直到模型改变）
                        recentCalls.Add((call.Name, call.Arguments));
                        var isDoomLoop = recentCalls.Count >= DoomLoopThreshold &&
                            recentCalls.TakeLast(DoomLoopThreshold).All(c => c == recentCalls[^1]);
                        if (isDoomLoop)
                        {
                            if (!loopStopped)
                            {
                                loopStopped = true;
                                messages.Add(new JsonObject
                                {
                                    ["role"] = "tool",
                                    ["tool_call_id"] = call.Id,
                                    ["content"] = $"警告：工具 {call.Name} 已用相同参数连续调用 {DoomLoopThreshold} 次仍未成功。" +
                                        "请停止重复调用，换一种策略，或直接向用户说明情况。",
                                });
                            }
                            continue; // 不再执行相同调用
                        }
                        onToolRunning?.Invoke(call.Name, call.Arguments);
                        var result = await executor(call.Name, call.Arguments); // 执行工具（可异步）
                        // 工具输出截断：防止长结果撑爆上下文
                        if (result.Length > ToolOutputMaxChars)
                            result = result[..ToolOutputMaxChars] + "…（输出已截断）";
                        messages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = call.Id,
                            ["content"] = result,
                        });
                    }
                    continue;
                }
                return new AIChatReply { Content = reply.Content ?? string.Empty };
            }

            // 工具轮次用尽：再请求一次（携带全部工具结果），若模型仍要工具则放弃
            var final = onDelta is null
                ? await SendOnceAsync(settings, payload, ct)
                : await SendOnceStreamingAsync(settings, payload, ct, onDelta);
            if (final.Error is not null)
                return new AIChatReply { Error = final.Error };
            return final.ToolCallsRequested
                ? new AIChatReply { Error = "工具调用轮次过多" }
                : new AIChatReply { Content = final.Content ?? string.Empty };
        }
        catch (OperationCanceledException)
        {
            return new AIChatReply { Error = "请求超时" };
        }
        catch (Exception ex)
        {
            return new AIChatReply { Error = $"请求失败: {ex.Message}" };
        }
    }

    private sealed record ToolCallInfo(string Id, string Name, string Arguments);

    private sealed record SendResult(string? Content, bool ToolCallsRequested,
        List<ToolCallInfo>? ToolCalls, string? Error, JsonObject? AssistantMessage = null);

    /// <summary>非流式单轮请求（含 429/5xx 指数退避重试，最多 2 次重试）。</summary>
    private async Task<SendResult> SendOnceAsync(AIChatSettings settings, JsonObject payload, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{settings.BaseUrl.TrimEnd('/')}/chat/completions");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {settings.ApiKey}");
            request.Content = new StringContent(
                payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                // 429/5xx 可重试：指数退避 1s/3s
                var status = (int)response.StatusCode;
                if (attempt < 2 && (status == 429 || status >= 500))
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 1 : 3), ct);
                    continue;
                }
                return new SendResult(null, false, null,
                    $"API 错误 {status}: {Truncate(body, 200)}");
            }

            return ParseResponse(body);
        }
    }

    /// <summary>SSE 流式单轮请求（onDelta 增量回调；工具调用 delta 按 index 聚合）。</summary>
    private async Task<SendResult> SendOnceStreamingAsync(AIChatSettings settings, JsonObject payload,
        CancellationToken ct, Action<string> onDelta)
    {
        payload["stream"] = true;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{settings.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {settings.ApiKey}");
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            return new SendResult(null, false, null,
                $"API 错误 {(int)response.StatusCode}: {Truncate(errBody, 200)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var content = new StringBuilder();
        var toolCalls = new Dictionary<int, ToolCallAccumulator>();
        string? finishReason = null;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
                break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;
            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                finishReason = fr.GetString();

            if (!choice.TryGetProperty("delta", out var delta))
                continue;
            if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                var chunk = c.GetString() ?? "";
                if (chunk.Length > 0)
                {
                    content.Append(chunk);
                    onDelta(chunk);
                }
            }
            if (delta.TryGetProperty("tool_calls", out var calls))
            {
                foreach (var call in calls.EnumerateArray())
                {
                    var index = call.TryGetProperty("index", out var i) ? i.GetInt32() : 0;
                    if (!toolCalls.TryGetValue(index, out var acc))
                        toolCalls[index] = acc = new ToolCallAccumulator();
                    if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        acc.Id = id.GetString() ?? "";
                    if (call.TryGetProperty("function", out var fn))
                    {
                        if (fn.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                            acc.Name.Append(n.GetString());
                        if (fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String)
                            acc.Arguments.Append(a.GetString());
                    }
                }
            }
        }

        // 完成工具 delta 聚合
        if (toolCalls.Count > 0)
        {
            var assistant = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = content.Length > 0 ? content.ToString() : null,
                ["tool_calls"] = new JsonArray(toolCalls.OrderBy(k => k.Key).Select(kv =>
                {
                    var acc = kv.Value;
                    return new JsonObject
                    {
                        ["id"] = acc.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = acc.Name.ToString(),
                            ["arguments"] = acc.Arguments.ToString(),
                        },
                    };
                }).ToArray()),
            };
            var calls = toolCalls.OrderBy(k => k.Key)
                .Select(kv => new ToolCallInfo(kv.Value.Id, kv.Value.Name.ToString(), kv.Value.Arguments.ToString()))
                .Where(c => c.Name.Length > 0)
                .ToList();
            return new SendResult(content.ToString(), true, calls, null, assistant);
        }

        return new SendResult(content.ToString(), false, null, null);
    }

    private sealed class ToolCallAccumulator
    {
        public string Id { get; set; } = "";
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
    }

    private static SendResult ParseResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return new SendResult(string.Empty, false, null, null);

        var message = choices[0].GetProperty("message");

        // 模型请求工具调用
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
        {
            var calls = new List<ToolCallInfo>();
            foreach (var call in toolCalls.EnumerateArray())
            {
                var callId = call.GetProperty("id").GetString() ?? "";
                var fn = call.GetProperty("function");
                var name = fn.GetProperty("name").GetString() ?? "";
                var args = fn.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";
                calls.Add(new ToolCallInfo(callId, name, args));
            }
            return new SendResult(null, true, calls, null,
                JsonNode.Parse(message.GetRawText())!.AsObject());
        }

        var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        return new SendResult(content ?? string.Empty, false, null, null);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    public void Dispose() => _http.Dispose();
}
