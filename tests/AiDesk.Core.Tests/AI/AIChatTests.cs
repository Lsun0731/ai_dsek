using System.Net;
using System.Net.Http;
using System.Text;
using AiDesk.Core.AI;

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
        var reply = await client.ChatAsync(new AIChatSettings { ApiKey = "k" }, "  ");

        Assert.True(reply.IsError);
        Assert.Contains("消息为空", reply.Error);
    }

    [Fact]
    public async Task ChatWithToolsAsync_工具调用回传并返回最终回复()
    {
        const string toolResponse = """
            {"choices":[{"message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"call_1","type":"function",
                "function":{"name":"launch_app","arguments":"{\"name\":\"记事本\"}"}}]}}]}
            """;
        const string finalResponse = """
            {"choices":[{"message":{"role":"assistant","content":"已启动应用「记事本」"}}]}
            """;
        using var client = new AIChatClient(new FakeHandler([toolResponse, finalResponse]));

        var calls = new List<(string Name, string Args)>();
        var reply = await client.ChatWithToolsAsync(
            new AIChatSettings { ApiKey = "k" },
            "帮我打开记事本",
            new[] { new AITool { Name = "launch_app", Description = "启动应用", ParametersJsonSchema = "{}" } },
            (name, args) =>
            {
                calls.Add((name, args));
                return Task.FromResult("ok");
            });

        Assert.False(reply.IsError);
        Assert.Equal("已启动应用「记事本」", reply.Content);
        var call = Assert.Single(calls);
        Assert.Equal("launch_app", call.Name);
        Assert.Contains("记事本", call.Args);
    }

    [Fact]
    public async Task ChatWithToolsAsync_工具结果回传进后续请求()
    {
        const string toolResponse = """
            {"choices":[{"message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"call_1","type":"function",
                "function":{"name":"get_system_info","arguments":"{}"}}]}}]}
            """;
        const string finalResponse = """
            {"choices":[{"message":{"role":"assistant","content":"CPU 使用率 12%"}}]}
            """;
        var handler = new FakeHandler([toolResponse, finalResponse]);
        using var client = new AIChatClient(handler);

        var reply = await client.ChatWithToolsAsync(
            new AIChatSettings { ApiKey = "k" },
            "电脑卡不卡",
            new[] { new AITool { Name = "get_system_info", Description = "系统信息", ParametersJsonSchema = "{}" } },
            (_, _) => Task.FromResult("CPU 12%"));

        Assert.False(reply.IsError);
        Assert.Equal("CPU 使用率 12%", reply.Content);

        // 第二次请求必须包含 assistant tool_calls 消息与 tool 结果
        var second = handler.RequestBodies[1];
        Assert.Contains("\"role\":\"assistant\"", second);
        Assert.Contains("\"tool_calls\"", second);
        Assert.Contains("\"role\":\"tool\"", second);
        Assert.Contains("\"tool_call_id\":\"call_1\"", second);
        Assert.Contains("CPU 12%", second);
    }

    [Fact]
    public async Task ChatWithToolsAsync_工具轮次超限_返回错误()
    {
        const string toolOnly = """
            {"choices":[{"message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"call_1","type":"function",
                "function":{"name":"loop","arguments":"{}"}}]}}]}
            """;
        // 4 轮工具 + 第 5 次仍要工具 → 放弃
        var responses = Enumerable.Repeat(toolOnly, 5);
        using var client = new AIChatClient(new FakeHandler(responses));

        var reply = await client.ChatWithToolsAsync(
            new AIChatSettings { ApiKey = "k" },
            "一直调用",
            new[] { new AITool { Name = "loop", Description = "循环", ParametersJsonSchema = "{}" } },
            (_, _) => Task.FromResult("done"));

        Assert.True(reply.IsError);
        Assert.Contains("工具调用轮次过多", reply.Error);
    }

    [Fact]
    public async Task ChatWithToolsAsync_重复相同调用_触发DoomLoop拦截()
    {
        const string toolResponse = """
            {"choices":[{"message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"call_1","type":"function",
                "function":{"name":"try_x","arguments":"{}"}}]}}]}
            """;
        const string finalResponse = """
            {"choices":[{"message":{"role":"assistant","content":"已换策略"}}]}
            """;
        // 4 次相同工具请求 + 最终回复
        var responses = Enumerable.Repeat(toolResponse, 4).Append(finalResponse);
        using var client = new AIChatClient(new FakeHandler(responses));

        var executed = 0;
        var reply = await client.ChatWithToolsAsync(
            new AIChatSettings { ApiKey = "k" },
            "重试",
            new[] { new AITool { Name = "try_x", Description = "试", ParametersJsonSchema = "{}" } },
            (_, _) => { executed++; return Task.FromResult("fail"); });

        Assert.False(reply.IsError);
        Assert.Equal("已换策略", reply.Content);
        // 前 2 次执行，第 3 次起被 Doom-loop 拦截（不再调 executor）
        Assert.Equal(2, executed);
    }

    [Fact]
    public async Task ChatWithToolsAsync_长工具输出_被截断()
    {
        const string toolResponse = """
            {"choices":[{"message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"call_1","type":"function",
                "function":{"name":"big","arguments":"{}"}}]}}]}
            """;
        const string finalResponse = """
            {"choices":[{"message":{"role":"assistant","content":"完成"}}]}
            """;
        var handler = new FakeHandler([toolResponse, finalResponse]);
        using var client = new AIChatClient(handler);

        var longOutput = new string('x', 3000);
        var reply = await client.ChatWithToolsAsync(
            new AIChatSettings { ApiKey = "k" },
            "大输出",
            new[] { new AITool { Name = "big", Description = "大", ParametersJsonSchema = "{}" } },
            (_, _) => Task.FromResult(longOutput));

        Assert.False(reply.IsError);
        // 工具结果截断：保留前 2000 字符（x 为 ASCII 不转义），更长的被截掉
        var second = handler.RequestBodies[1];
        Assert.Contains(new string('x', 2000), second);
        Assert.DoesNotContain(new string('x', 2500), second);
    }

    [Fact]
    public async Task ChatWithToolsAsync_临时错误_自动重试成功()
    {
        const string okResponse = """
            {"choices":[{"message":{"role":"assistant","content":"重试成功"}}]}
            """;
        var handler = new FakeHandler([]);
        handler.AddStatus(429);      // 第一次 429
        handler.AddBody(okResponse); // 重试成功
        using var client = new AIChatClient(handler);

        var reply = await client.ChatWithToolsAsync(
            new AIChatSettings { ApiKey = "k" },
            "你好",
            [],
            (_, _) => Task.FromResult(""));

        Assert.False(reply.IsError);
        Assert.Equal("重试成功", reply.Content);
        Assert.Equal(2, handler.RequestBodies.Count); // 1 次失败 + 1 次重试
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<string> RequestBodies { get; } = [];

        public FakeHandler(IEnumerable<string> responses)
        {
            foreach (var body in responses)
                AddBody(body);
        }

        /// <summary>追加一条 200 响应。</summary>
        public void AddBody(string json) => _responses.Enqueue(() => Ok(json));

        /// <summary>追加一条指定状态码的响应（用于测试重试）。</summary>
        public void AddStatus(int status)
            => _responses.Enqueue(() => new HttpResponseMessage((HttpStatusCode)status));

        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content?.ReadAsStringAsync(ct).Result ?? "";
            RequestBodies.Add(body);
            var respond = _responses.Count > 0 ? _responses.Dequeue() : () => Ok("{}");
            return Task.FromResult(respond());
        }
    }
}
