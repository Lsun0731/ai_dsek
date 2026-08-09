using System.Net.Http;
using System.Text;
using System.Text.Json;

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
    public string SystemPrompt { get; set; } = "你是一个活泼可爱的桌面宠物 AI 助手，回答简短友好，语气俏皮。";
}

/// <summary>AI 对话回复。</summary>
public sealed record AIChatReply
{
    public string? Content { get; init; }
    public string? Error { get; init; }
    public bool IsError => Error is not null;
}

/// <summary>
/// OpenAI 兼容 Chat Completions 客户端（DeepSeek/智谱/OpenAI 等均适用）。
/// </summary>
public sealed class AIChatClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>发送单轮对话，返回回复内容。失败返回带 Error 的回复。</summary>
    public async Task<AIChatReply> ChatAsync(AIChatSettings settings, string userMessage,
        IReadOnlyList<(string Role, string Content)>? history = null, CancellationToken ct = default)
    {
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

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    public void Dispose() => _http.Dispose();
}
