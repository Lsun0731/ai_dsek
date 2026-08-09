using System.Diagnostics;
using System.Text.Json;
using AiDesk.Core.AI;
using AiDesk.Core.Widgets;

namespace AiDesk.App.Services;

/// <summary>
/// AI Agent 工具（宠物与搜索面板共用）：启动应用、查询系统信息。
/// </summary>
public static class AgentTools
{
    /// <summary>工具定义（OpenAI function calling）。</summary>
    public static IReadOnlyList<AITool> Tools { get; } = new[]
    {
        new AITool
        {
            Name = "launch_app",
            Description = "启动一个已安装的应用程序（按名称匹配，如：记事本、计算器、Chrome、设置）。",
            ParametersJsonSchema = """
                {"type":"object","properties":{"name":{"type":"string","description":"应用名称关键词"}},"required":["name"]}
                """,
        },
        new AITool
        {
            Name = "get_system_info",
            Description = "查询电脑系统信息：操作系统版本、CPU 使用率、内存、磁盘占用。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },
    };

    private static readonly Lazy<IReadOnlyList<StartMenuApp>> AppsCache =
        new(() => StartMenuAppsProvider.Scan());

    private static readonly Lazy<SystemStatsProvider> Stats = new(() => new SystemStatsProvider());

    /// <summary>执行工具调用，返回工具结果文本。</summary>
    public static string Execute(string name, string argumentsJson)
    {
        try
        {
            return name switch
            {
                "launch_app" => ExecuteLaunchApp(argumentsJson),
                "get_system_info" => GetSystemInfo(),
                _ => $"未知工具: {name}",
            };
        }
        catch (Exception ex)
        {
            return $"工具执行失败: {ex.Message}";
        }
    }

    private static string ExecuteLaunchApp(string argumentsJson)
    {
        string keyword;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            keyword = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        }
        catch
        {
            keyword = "";
        }
        if (string.IsNullOrWhiteSpace(keyword))
            return "缺少应用名称参数";

        var app = AppsCache.Value
            .FirstOrDefault(a => a.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        if (app is null)
            return $"未找到应用「{keyword}」。可用名称如：记事本、计算器、设置、画图、截图工具。";

        Process.Start(new ProcessStartInfo(app.LnkPath) { UseShellExecute = true });
        return $"已启动应用「{app.Name}」";
    }

    private static string GetSystemInfo()
    {
        var stats = Stats.Value.Sample();
        var os = Environment.OSVersion.VersionString;
        var cpu = $"{stats.CpuPercent:F0}%";
        var mem = $"{stats.MemPercent:F0}% 已用（{stats.MemUsedGb:F1}/{stats.MemTotalGb:F1} GB）";
        var disks = string.Join("；", stats.Disks.Select(d => $"{d.Name} {d.Percent:F0}% 已用"));
        return $"操作系统: {os}；CPU 使用率: {cpu}；内存: {mem}；磁盘: {disks}";
    }
}
