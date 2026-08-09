using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiDesk.Core.AI;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Services;

/// <summary>
/// AI 对话会话服务：宠物与搜索面板共用。
/// 统一管理多轮历史（上限 20 条，回复后裁剪）、上下文压缩（token 估算 + 摘要化旧轮次）、
/// 会话持久化（重启恢复）、发送流程、错误分支与埋点。纯文字约束（强制 prompt + TextSanitizer 净化）。
/// </summary>
public sealed class ChatSessionService : IDisposable
{
    private readonly AIChatClient _ai = new();
    private List<(string Role, string Content)> _history = new();
    private string? _summary;
    private readonly string _storePath;
    private const int MaxHistory = 20;
    private const int CompactTokenThreshold = 3000;
    private bool _busy;

    /// <summary>
    /// 强制纯文字系统提示（覆盖磁盘可能残留的旧 prompt；模型不遵守时由 TextSanitizer 兜底）。
    /// </summary>
    private const string ForcedSystemPrompt =
        "你是一个友好的桌面宠物 AI 助手。你的回复必须严格遵守：1) 只使用纯文字，禁止任何 emoji、表情符号、颜文字、图标或符号装饰；" +
        "2) 简洁友好；3) 可以使用工具帮用户处理电脑问题（启动应用、查询系统信息、联网搜索等）；" +
        "4) 复杂任务主动拆解成多步依次调用工具完成；5) 信息不足时先调用工具获取，或向用户提问确认。";

    public ChatSessionService(string sessionKey)
    {
        _storePath = Path.Combine(AppConfig.DataDirectory, $"chat-{sessionKey}.json");
        Load();
    }

    /// <summary>是否正在请求中（防并发发送）。</summary>
    public bool IsBusy => _busy;

    /// <summary>
    /// 发送消息并管理历史。
    /// onAssistant 在收到回复（含错误文本）后于调用线程回调，由窗口负责 UI 展示。
    /// onDelta：流式增量文本（逐字回调）；onToolRunning：工具执行前回调（进度反馈）。
    /// 返回回复内容；出错返回 null（错误文本已通过 onAssistant 展示）。
    /// </summary>
    public async Task<string?> SendAsync(string message, Action<string> onAssistant, string telemetryName,
        Action<string>? onDelta = null, Action<string, string>? onToolRunning = null)
    {
        if (_busy || string.IsNullOrWhiteSpace(message))
            return null;

        _busy = true;
        try
        {
            _history.Add(("user", message));
            await CompactIfNeededAsync();

            var settings = AppConfig.Load().AI;
            settings.SystemPrompt = ForcedSystemPrompt; // 覆盖磁盘旧值，确保纯文字约束生效
            var reply = await _ai.ChatWithToolsAsync(
                settings, message, AgentTools.Tools, AgentTools.ExecuteAsync, EffectiveHistory(),
                ct: default, onDelta: onDelta, onToolRunning: onToolRunning);

            if (reply.IsError)
            {
                var error = reply.Error ?? "出错了";
                onAssistant(error);
                Telemetry.Function(telemetryName, false, 0, $"err={reply.Error}");
                return null;
            }

            // 客户端净化兜底：删除回复中的 emoji/符号/颜文字（模型不遵守 prompt 时也保证纯文字）
            var content = TextSanitizer.Sanitize(reply.Content);
            _history.Add(("assistant", content));
            if (_history.Count > MaxHistory)
                _history.RemoveRange(0, _history.Count - MaxHistory);
            onAssistant(content);
            Telemetry.Function(telemetryName, true, 0, $"len={content.Length}");
            Save();
            return content;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>清空会话历史（退出对话时调用）。</summary>
    public void Clear()
    {
        _history.Clear();
        _summary = null;
        try
        {
            if (File.Exists(_storePath))
                File.Delete(_storePath);
        }
        catch
        {
            // 忽略
        }
    }

    public void Dispose() => _ai.Dispose();

    // ---- 上下文压缩 ----

    /// <summary>有效历史：摘要作为 system 前缀 + 最近轮次。</summary>
    private IReadOnlyList<(string Role, string Content)> EffectiveHistory()
    {
        if (string.IsNullOrWhiteSpace(_summary))
            return _history;
        var list = new List<(string, string)> { ("system", $"对话摘要：{_summary}") };
        list.AddRange(_history);
        return list;
    }

    /// <summary>token 估算（中文约 1 token/1.5 字符，按 2 字符/token 保守估算）。</summary>
    private static int EstimateTokens(IEnumerable<(string Role, string Content)> messages)
        => (int)(messages.Sum(m => m.Role.Length + m.Content.Length) / 2.0);

    /// <summary>上下文超阈值时：旧轮次交给模型生成摘要，保留最近 2 轮原文。</summary>
    private async Task CompactIfNeededAsync()
    {
        if (_history.Count <= 4 || EstimateTokens(_history) < CompactTokenThreshold)
            return;

        var keep = _history.TakeLast(4).ToList();
        var old = _history.Take(_history.Count - 4).ToList();
        var oldText = string.Join("\n", old.Select(m => $"{m.Role}: {m.Content}"));

        var settings = AppConfig.Load().AI;
        settings.SystemPrompt =
            "把下面的对话压缩成简洁的中文摘要，保留关键信息：用户意图、已执行的操作、得出的结论。只输出摘要本身，不要任何解释。";
        var reply = await _ai.ChatAsync(settings, oldText);

        if (!reply.IsError && !string.IsNullOrWhiteSpace(reply.Content))
        {
            _summary = TextSanitizer.Sanitize(reply.Content);
            if (_summary.Length > 1200)
                _summary = _summary[..1200] + "…";
        }
        _history = keep;
        Telemetry.Function("Chat.Compact", !reply.IsError, 0, $"old={old.Count} keep={keep.Count}");
    }

    // ---- 会话持久化 ----

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath))
                return;
            using var doc = JsonDocument.Parse(File.ReadAllText(_storePath));
            var root = doc.RootElement;
            if (root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String)
                _summary = s.GetString();
            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
            {
                _history = msgs.EnumerateArray()
                    .Select(m => (m.GetProperty("role").GetString() ?? "", m.GetProperty("content").GetString() ?? ""))
                    .Where(m => m.Item1.Length > 0 && m.Item2.Length > 0)
                    .ToList();
            }
        }
        catch
        {
            _history = [];
        }
    }

    private void Save()
    {
        try
        {
            var obj = new JsonObject
            {
                ["summary"] = _summary,
                ["messages"] = new JsonArray(_history.Select(m => new JsonObject
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content,
                }).ToArray()),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            var tmp = _storePath + ".tmp";
            File.WriteAllText(tmp, obj.ToJsonString());
            File.Replace(tmp, _storePath, null);
        }
        catch
        {
            // 落盘失败不影响对话
        }
    }
}
