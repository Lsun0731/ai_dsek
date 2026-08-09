using AiDesk.Core.AI;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Services;

/// <summary>
/// AI 对话会话服务：宠物与搜索面板共用。
/// 统一管理多轮历史（上限 20 条，回复后裁剪）、发送流程、错误分支与埋点。
/// </summary>
public sealed class ChatSessionService : IDisposable
{
    private readonly AIChatClient _ai = new();
    private readonly List<(string Role, string Content)> _history = new();
    private const int MaxHistory = 20;
    private bool _busy;

    /// <summary>
    /// 强制纯文字系统提示（覆盖磁盘可能残留的旧 prompt；模型不遵守时由 TextSanitizer 兜底）。
    /// </summary>
    private const string ForcedSystemPrompt =
        "你是一个友好的桌面宠物 AI 助手。你的回复必须严格遵守：1) 只使用纯文字，禁止任何 emoji、表情符号、颜文字、图标或符号装饰；" +
        "2) 简洁友好；3) 可以使用工具帮用户处理电脑问题（启动应用、查询系统信息、联网搜索等）；" +
        "4) 复杂任务主动拆解成多步依次调用工具完成；5) 信息不足时先调用工具获取，或向用户提问确认。";

    /// <summary>是否正在请求中（防并发发送）。</summary>
    public bool IsBusy => _busy;

    /// <summary>
    /// 发送消息并管理历史。
    /// onAssistant 在收到回复（含错误文本）后于调用线程回调，由窗口负责 UI 展示。
    /// 返回回复内容；出错返回 null（错误文本已通过 onAssistant 展示）。
    /// </summary>
    public async Task<string?> SendAsync(string message, Action<string> onAssistant, string telemetryName)
    {
        if (_busy || string.IsNullOrWhiteSpace(message))
            return null;

        _busy = true;
        try
        {
            _history.Add(("user", message));

            var settings = AppConfig.Load().AI;
            settings.SystemPrompt = ForcedSystemPrompt; // 覆盖磁盘旧值，确保纯文字约束生效
            var reply = await _ai.ChatWithToolsAsync(
                settings, message, AgentTools.Tools, AgentTools.ExecuteAsync, _history);

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
            return content;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>清空会话历史（退出对话时调用）。</summary>
    public void Clear() => _history.Clear();

    public void Dispose() => _ai.Dispose();
}
