using AiDesk.Core.AI;

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

    /// <summary>桌面宠物（程序化角色 + AI 对话联动）</summary>
    Pet,
}

/// <summary>单个小组件的持久化状态。</summary>
public sealed class WidgetState
{
    public double Left { get; set; }
    public double Top { get; set; }
    public bool IsOpen { get; set; }
}

/// <summary>应用统一配置（全部模块共用一份 settings.json）。</summary>
public sealed class AppSettings
{
    /// <summary>小组件全局透明度（0.3-1.0）。</summary>
    public double Opacity { get; set; } = 0.9;

    /// <summary>天气城市。</summary>
    public string WeatherCity { get; set; } = "北京";

    /// <summary>各小组件状态（按类型名）。</summary>
    public Dictionary<string, WidgetState> Widgets { get; set; } = new();

    /// <summary>AI 对话配置（宠物联动用）。</summary>
    public AIChatSettings AI { get; set; } = new();

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
