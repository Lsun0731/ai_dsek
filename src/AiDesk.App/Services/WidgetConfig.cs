using System.IO;
using System.Text.Json;

namespace AiDesk.App.Services;

/// <summary>小组件类型。</summary>
public enum WidgetKind
{
    /// <summary>系统状态（CPU/内存/磁盘/网络）</summary>
    Stats,

    /// <summary>日期</summary>
    Date,

    /// <summary>天气</summary>
    Weather,

    /// <summary>音乐监控（当前媒体会话元数据 + 播放控制）</summary>
    Music,

    /// <summary>应用搜索（启动开始菜单应用）</summary>
    Search,
}

/// <summary>单个小组件的持久化状态。</summary>
public sealed class WidgetState
{
    public double Left { get; set; }
    public double Top { get; set; }
    public bool IsOpen { get; set; }
}

/// <summary>小组件全局配置。</summary>
public sealed class WidgetSettings
{
    /// <summary>全局透明度（0.3-1.0，作用于所有小组件）。</summary>
    public double Opacity { get; set; } = 0.9;

    /// <summary>天气城市。</summary>
    public string WeatherCity { get; set; } = "北京";

    /// <summary>各小组件状态（按类型名）。</summary>
    public Dictionary<string, WidgetState> Widgets { get; set; } = new();

    public WidgetState GetState(WidgetKind kind)
    {
        var key = kind.ToString();
        if (!Widgets.TryGetValue(key, out var state))
        {
            state = new WidgetState { Left = 80 + Widgets.Count * 40, Top = 80 + Widgets.Count * 30 };
            Widgets[key] = state;
        }
        return state;
    }
}

/// <summary>小组件配置持久化（%LOCALAPPDATA%\AiDesk\config.json）。</summary>
public static class WidgetConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiDesk", "config.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<WidgetSettings>(json);
                if (settings is not null)
                    return settings;
            }
        }
        catch
        {
            // 配置损坏时使用默认值
        }
        return new WidgetSettings();
    }

    public static void Save(WidgetSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不影响运行
        }
    }
}
