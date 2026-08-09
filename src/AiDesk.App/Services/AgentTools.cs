using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using AiDesk.Core.AI;
using AiDesk.Core.Desktop;

namespace AiDesk.App.Services;

/// <summary>
/// AI Agent 工具注册表：14 个电脑助手工具，按安全分级（查询 / 操作 / 危险需确认）。
/// 宠物与搜索面板共用。执行分发见 AgentToolsSystem（partial）。
/// </summary>
public static partial class AgentTools
{
    /// <summary>工具定义（OpenAI function calling）。</summary>
    public static IReadOnlyList<AITool> Tools { get; } = new[]
    {
        // ---- 查询（只读） ----
        new AITool
        {
            Name = "get_system_info",
            Description = "查询电脑系统信息：操作系统、CPU、内存、磁盘占用。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },
        new AITool
        {
            Name = "list_processes",
            Description = "列出正在运行的进程（按关键词过滤，含内存占用）。",
            ParametersJsonSchema = """{"type":"object","properties":{"filter":{"type":"string","description":"可选：按名称过滤，如 chrome"}}}""",
        },
        new AITool
        {
            Name = "disk_usage",
            Description = "查询各磁盘的已用/剩余空间。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },
        new AITool
        {
            Name = "get_network_info",
            Description = "查询网络状态：IP 地址、是否联网、连接类型。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },
        new AITool
        {
            Name = "ping_host",
            Description = "测试网络延迟（ping 一个域名或 IP）。",
            ParametersJsonSchema = """{"type":"object","properties":{"host":{"type":"string","description":"域名或 IP，如 baidu.com"}},"required":["host"]}""",
        },
        new AITool
        {
            Name = "search_files",
            Description = "按文件名关键词搜索用户目录下的文件。",
            ParametersJsonSchema = """{"type":"object","properties":{"keyword":{"type":"string"},"folder":{"type":"string","description":"可选：指定目录，默认用户目录"}},"required":["keyword"]}""",
        },

        // ---- 操作（直接执行） ----
        new AITool
        {
            Name = "launch_app",
            Description = "启动一个已安装的应用程序（按名称匹配，如：记事本、计算器、Chrome、设置）。",
            ParametersJsonSchema = """{"type":"object","properties":{"name":{"type":"string","description":"应用名称关键词"}},"required":["name"]}""",
        },
        new AITool
        {
            Name = "open_path",
            Description = "打开文件或文件夹（资源管理器）。",
            ParametersJsonSchema = """{"type":"object","properties":{"path":{"type":"string","description":"完整路径"}},"required":["path"]}""",
        },
        new AITool
        {
            Name = "open_website",
            Description = "在默认浏览器打开网址。",
            ParametersJsonSchema = """{"type":"object","properties":{"url":{"type":"string","description":"完整网址，如 https://www.bing.com"}},"required":["url"]}""",
        },
        new AITool
        {
            Name = "open_system_settings",
            Description = "打开 Windows 设置页（page 可选：display/network/volume/sound/about/storage/apps/bluetooth/notifications）。",
            ParametersJsonSchema = """{"type":"object","properties":{"page":{"type":"string","description":"设置页名称"}}}""",
        },
        new AITool
        {
            Name = "set_wallpaper",
            Description = "设置桌面壁纸（需要图片完整路径）。",
            ParametersJsonSchema = """{"type":"object","properties":{"path":{"type":"string","description":"图片完整路径"}},"required":["path"]}""",
        },
        new AITool
        {
            Name = "clipboard_write",
            Description = "把文本写入剪贴板。",
            ParametersJsonSchema = """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}""",
        },
        new AITool
        {
            Name = "set_volume",
            Description = "设置系统音量（0-100）。",
            ParametersJsonSchema = """{"type":"object","properties":{"level":{"type":"number","description":"0 到 100"}},"required":["level"]}""",
        },
        new AITool
        {
            Name = "toggle_mute",
            Description = "切换静音/取消静音。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },
        new AITool
        {
            Name = "screenshot",
            Description = "截取整个屏幕并保存到图片目录。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },

        // ---- AiDesk 应用联动 ----
        new AITool
        {
            Name = "open_search_panel",
            Description = "打开 AiDesk 自己的搜索面板（搜索应用）。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },
        new AITool
        {
            Name = "open_clipboard",
            Description = "打开 AiDesk 剪贴板面板（历史记录）。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },
        new AITool
        {
            Name = "open_main_window",
            Description = "打开 AiDesk 主窗口（工具箱）。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
        },

        // ---- 危险（需用户确认） ----
        new AITool
        {
            Name = "kill_process",
            Description = "结束指定名称的进程（如 chrome）。",
            ParametersJsonSchema = """{"type":"object","properties":{"name":{"type":"string","description":"进程名称（不含 .exe）"}},"required":["name"]}""",
            RequireConfirm = true,
        },
        new AITool
        {
            Name = "clear_temp",
            Description = "清理系统临时文件（释放磁盘空间）。",
            ParametersJsonSchema = """{"type":"object","properties":{}}""",
            RequireConfirm = true,
        },
    };

    /// <summary>执行工具调用；危险工具先弹确认框。</summary>
    public static string Execute(string name, string argumentsJson)
    {
        var tool = Tools.FirstOrDefault(t => t.Name == name);
        if (tool is null)
            return $"未知工具: {name}";

        if (tool.RequireConfirm)
        {
            var summary = Summarize(tool, argumentsJson);
            var confirmed = Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show($"确定要执行「{summary}」吗？", "AiDesk 操作确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
            if (!confirmed)
                return "用户取消了该操作";
        }

        try
        {
            return Dispatch(name, argumentsJson);
        }
        catch (Exception ex)
        {
            return $"工具执行失败: {ex.Message}";
        }
    }

    /// <summary>把参数解析成可读摘要（确认框展示）。</summary>
    private static string Summarize(AITool tool, string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var parts = doc.RootElement.EnumerateObject()
                .Select(p => $"{p.Name}={p.Value}")
                .ToList();
            return parts.Count == 0 ? tool.Name : $"{tool.Name}（{string.Join(", ", parts)}）";
        }
        catch
        {
            return tool.Name;
        }
    }

    /// <summary>参数解析辅助：取字符串参数。</summary>
    private static string? GetStringArg(string argumentsJson, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>参数解析辅助：取数值参数（返回 -1 表示缺省）。</summary>
    private static double GetNumberArg(string argumentsJson, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            return doc.RootElement.TryGetProperty(key, out var v) ? v.GetDouble() : -1;
        }
        catch
        {
            return -1;
        }
    }

    // ---- 基础工具实现（系统类见 AgentToolsSystem.cs） ----

    private static string ExecuteLaunchApp(string argumentsJson)
    {
        var keyword = GetStringArg(argumentsJson, "name")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(keyword))
            return "缺少应用名称参数";

        var app = AppsCache.Value
            .FirstOrDefault(a => a.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        if (app is null)
            return $"未找到应用「{keyword}」。可用名称如：记事本、计算器、设置、画图、截图工具。";

        Process.Start(new ProcessStartInfo(app.LnkPath) { UseShellExecute = true });
        return $"已启动应用「{app.Name}」";
    }

    private static string ExecuteOpenPath(string argumentsJson)
    {
        var path = GetStringArg(argumentsJson, "path")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(path))
            return "缺少路径参数";
        if (Directory.Exists(path))
        {
            Process.Start("explorer.exe", $"\"{path}\"");
            return $"已打开文件夹 {path}";
        }
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return $"已打开文件 {path}";
        }
        return $"路径不存在：{path}";
    }

    private static string ExecuteOpenWebsite(string argumentsJson)
    {
        var url = GetStringArg(argumentsJson, "url")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
            return "缺少网址参数";
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return $"已打开 {url}";
    }

    private static readonly string[] SettingsPageWhitelist =
    {
        "display", "network", "volume", "sound", "about", "storage", "apps", "bluetooth", "notifications",
    };

    private static string ExecuteOpenSettings(string argumentsJson)
    {
        var page = GetStringArg(argumentsJson, "page")?.Trim() ?? "";
        if (page.Length > 0 && !SettingsPageWhitelist.Contains(page, StringComparer.OrdinalIgnoreCase))
            return $"不支持的设置页「{page}」，可用：{string.Join("、", SettingsPageWhitelist)}";
        var uri = page.Length > 0 ? $"ms-settings:{page.ToLowerInvariant()}" : "ms-settings:";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return page.Length > 0 ? $"已打开设置：{page}" : "已打开 Windows 设置";
    }

    private static string ExecuteSetWallpaper(string argumentsJson)
    {
        var path = GetStringArg(argumentsJson, "path")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return $"图片不存在：{path}";
        try
        {
            using var wallpaper = new WallpaperService();
            wallpaper.SetWallpaper(path, WallpaperStyle.Fill);
            return $"壁纸已设置为 {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            return $"设置壁纸失败: {ex.Message}";
        }
    }

    private static string ExecuteClipboardWrite(string argumentsJson)
    {
        var text = GetStringArg(argumentsJson, "text") ?? "";
        try
        {
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
            return $"已复制到剪贴板（{text.Length} 字符）";
        }
        catch (Exception ex)
        {
            return $"写入剪贴板失败: {ex.Message}";
        }
    }

    /// <summary>开始菜单应用缓存（避免重复全量扫描）。</summary>
    private static readonly Lazy<IReadOnlyList<StartMenuApp>> AppsCache =
        new(() => StartMenuAppsProvider.Scan());
}
