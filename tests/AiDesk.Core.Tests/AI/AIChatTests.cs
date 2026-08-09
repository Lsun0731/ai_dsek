using System.Net;
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
                return "ok";
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
            (_, _) => "CPU 12%");

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
            (_, _) => "done");

        Assert.True(reply.IsError);
        Assert.Contains("工具调用轮次过多", reply.Error);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public List<string> RequestBodies { get; } = [];

        public FakeHandler(IEnumerable<string> responses) => _responses = new Queue<string>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content?.ReadAsStringAsync(ct).Result ?? "";
            RequestBodies.Add(body);
            var json = _responses.Count > 0 ? _responses.Dequeue() : "{}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
