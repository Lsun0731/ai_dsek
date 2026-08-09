using AiDesk.Core.AI;
using AiDesk.Core.Clipboard;

namespace AiDesk.Core.Tests.AI;

public class AIChatClientTests
{
    [Fact]
    public async Task ChatAsync_无Key_返回配置错误()
    {
        using var client = new AIChatClient();
        var reply = await client.ChatAsync(new AIChatSettings { ApiKey = "" }, "你好");

        Assert.True(reply.IsError);
        Assert.Contains("API Key", reply.Error);
    }

    [Fact]
    public async Task ChatAsync_空消息_返回错误()
    {
        using var client = new AIChatClient();
        var reply = await client.ChatAsync(new AIChatSettings { ApiKey = "test" }, "  ");

        Assert.True(reply.IsError);
    }
}

public class ClipboardMonitorTests
{
    [Fact]
    public void 创建与读取历史_不抛异常()
    {
        using var monitor = new ClipboardMonitor(pollIntervalMs: 200);
        Thread.Sleep(300); // 让轮询跑一轮
        var history = monitor.History; // 剪贴板可能为空，但读取不应抛异常
        Assert.NotNull(history);
    }
}
