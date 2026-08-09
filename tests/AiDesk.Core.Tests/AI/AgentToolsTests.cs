using System.Text.Json;
using AiDesk.App.Services;

namespace AiDesk.Core.Tests.AI;

public class AgentToolsTests
{
    [Fact]
    public void Tools_包含全部注册工具_定义完整()
    {
        var names = AgentTools.Tools.Select(t => t.Name).ToList();

        // 查询类
        Assert.Contains("get_system_info", names);
        Assert.Contains("list_processes", names);
        Assert.Contains("disk_usage", names);
        Assert.Contains("get_network_info", names);
        Assert.Contains("ping_host", names);
        Assert.Contains("search_files", names);
        // 操作类
        Assert.Contains("launch_app", names);
        Assert.Contains("open_path", names);
        Assert.Contains("open_website", names);
        Assert.Contains("open_system_settings", names);
        Assert.Contains("set_wallpaper", names);
        Assert.Contains("clipboard_write", names);
        Assert.Contains("set_volume", names);
        Assert.Contains("toggle_mute", names);
        Assert.Contains("screenshot", names);
        // 危险类（需确认）
        Assert.Contains("kill_process", names);
        Assert.Contains("clear_temp", names);

        // 所有工具 schema 必须是合法 JSON
        foreach (var tool in AgentTools.Tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} 缺描述");
            using var doc = JsonDocument.Parse(tool.ParametersJsonSchema);
            Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
        }

        // 危险工具标记确认
        Assert.True(AgentTools.Tools.First(t => t.Name == "kill_process").RequireConfirm);
        Assert.True(AgentTools.Tools.First(t => t.Name == "clear_temp").RequireConfirm);
        Assert.False(AgentTools.Tools.First(t => t.Name == "launch_app").RequireConfirm);
    }

    [Fact]
    public void Execute_未知工具_返回错误()
    {
        var result = AgentTools.Execute("no_such_tool", "{}");
        Assert.Contains("未知工具", result);
    }

    [Fact]
    public void Execute_磁盘占用_返回盘符信息()
    {
        var result = AgentTools.Execute("disk_usage", "{}");
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("工具执行失败", result);
    }

    [Fact]
    public void Execute_系统信息_返回关键字段()
    {
        var result = AgentTools.Execute("get_system_info", "{}");
        Assert.Contains("操作系统", result);
        Assert.Contains("内存", result);
    }

    [Fact]
    public void Execute_搜索文件_参数校验()
    {
        // 缺 keyword → 报参数错误
        var noArg = AgentTools.Execute("search_files", "{}");
        Assert.Contains("keyword", noArg);

        // 指定不存在目录 → 报目录错误
        var badDir = AgentTools.Execute("search_files",
            """{"keyword":"x","folder":"Z:\\不存在的目录_ai_desk_test"}""");
        Assert.Contains("目录不存在", badDir);
    }

    [Fact]
    public void Execute_音量参数越界_返回错误()
    {
        var result = AgentTools.Execute("set_volume", """{"level":150}""");
        Assert.Contains("0-100", result);
    }

    [Fact]
    public void Execute_打开设置_白名单外页面_返回提示()
    {
        var result = AgentTools.Execute("open_system_settings", """{"page":"hack"}""");
        Assert.Contains("不支持的设置页", result);
    }

    [Fact]
    public void Tools_包含联网搜索与复合工具()
    {
        var names = AgentTools.Tools.Select(t => t.Name).ToList();
        Assert.Contains("web_search", names);
        Assert.Contains("computer_health_check", names);
        Assert.Contains("cleanup_computer", names);
        Assert.True(AgentTools.Tools.First(t => t.Name == "cleanup_computer").RequireConfirm);
    }

    [Fact]
    public void ParseBingResults_解析结果块()
    {
        const string html = """
            <html><body>
            <li class="b_algo"><h2><a href="https://example.com/a">标题 A</a></h2><p>摘要 <b>A</b></p></li>
            <li class="b_algo b_attribution"><h2><a href="https://example.com/b">标题 B</a></h2><p>摘要 B</p></li>
            <li class="b_pag">分页无关</li>
            </body></html>
            """;
        var results = AgentTools.ParseBingResults(html);

        Assert.Equal(2, results.Count);
        Assert.Contains("标题 A", results[0]);
        Assert.Contains("https://example.com/a", results[0]);
        Assert.Contains("标题 B", results[1]); // b_algo b_attribution 变体也匹配
    }

    [Fact]
    public async Task ExecuteAsync_联网搜索_缺参数报错()
    {
        var result = await AgentTools.ExecuteAsync("web_search", "{}");
        Assert.Contains("query", result);
    }

    [Fact]
    public async Task ExecuteAsync_电脑体检_返回报告()
    {
        var result = await AgentTools.ExecuteAsync("computer_health_check", "{}");
        Assert.Contains("操作系统", result);
        Assert.DoesNotContain("工具执行失败", result);
    }
}
