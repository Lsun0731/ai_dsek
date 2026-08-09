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
            var reply = await _ai.ChatWithToolsAsync(
                settings, message, AgentTools.Tools, AgentTools.ExecuteAsync, _history);

            if (reply.IsError)
            {
                var error = reply.Error ?? "出错了";
                onAssistant(error);
                Telemetry.Function(telemetryName, false, 0, $"err={reply.Error}");
                return null;
            }

            var content = reply.Content ?? "";
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
