using AiDesk.App.Services;
using AiDesk.App.Widgets;
using AiDesk.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiDesk.App.ViewModels;

/// <summary>
/// 桌面小组件页 ViewModel：独立控制 系统状态 / 日期 / 天气 三个小组件。
/// </summary>
public partial class WidgetViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<WidgetKind, WidgetWindowBase> _windows = [];

    [ObservableProperty]
    private bool _statsOpen;

    [ObservableProperty]
    private bool _dateOpen;

    [ObservableProperty]
    private bool _weatherOpen;

    [ObservableProperty]
    private bool _musicOpen;

    [ObservableProperty]
    private bool _searchOpen;

    [ObservableProperty]
    private double _widgetOpacity = 0.9;

    [ObservableProperty]
    private string _weatherCity = "北京";

    public WidgetViewModel()
    {
        var settings = AppConfig.Load();
        WidgetOpacity = settings.Opacity;
        WeatherCity = settings.WeatherCity;
        StatsOpen = settings.GetState(WidgetKind.Stats).IsOpen;
        DateOpen = settings.GetState(WidgetKind.Date).IsOpen;
        WeatherOpen = settings.GetState(WidgetKind.Weather).IsOpen;
        MusicOpen = settings.GetState(WidgetKind.Music).IsOpen;
        SearchOpen = settings.GetState(WidgetKind.Search).IsOpen;
    }

    private bool _suppressToggle;

    partial void OnStatsOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Stats, value, () => new StatsWidgetWindow());
    }

    partial void OnDateOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Date, value, () => new DateWidgetWindow());
    }

    partial void OnWeatherOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Weather, value, () => new WeatherWidgetWindow());
    }

    partial void OnMusicOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Music, value, () => new MusicWidgetWindow());
    }

    partial void OnSearchOpenChanged(bool value)
    {
        if (!_suppressToggle)
            ToggleWidget(WidgetKind.Search, value, () => new SearchWidgetWindow());
    }

    /// <summary>热键 Ctrl+Alt+D 呼出/隐藏搜索小组件。</summary>
    public void ToggleSearch() => SearchOpen = !SearchOpen;

    partial void OnWidgetOpacityChanged(double value)
    {
        // 局部更新：基于磁盘最新配置只改 Opacity，避免快照整体覆写其他模块写入
        var settings = AppConfig.Load();
        settings.Opacity = value;
        AppConfig.Save(settings);
        foreach (var window in _windows.Values)
            window.SetWidgetOpacity(value);
    }

    partial void OnWeatherCityChanged(string value)
    {
        var settings = AppConfig.Load();
        settings.WeatherCity = value;
        AppConfig.Save(settings);
        if (_windows.TryGetValue(WidgetKind.Weather, out var weather))
            weather.RefreshNow();
    }

    private void ToggleWidget(WidgetKind kind, bool open, Func<WidgetWindowBase> factory)
    {
        if (open)
        {
            if (_windows.ContainsKey(kind))
                return;
            var window = factory();
            window.Closed += (_, _) =>
            {
                _windows.Remove(kind);
                SetOpenFlagSilently(kind, false);
            };
            _windows[kind] = window;
            // 持久化打开状态，重启后记住已打开的组件（局部更新，不覆写其他字段）
            var settings = AppConfig.Load();
            settings.GetState(kind).IsOpen = true;
            AppConfig.Save(settings);
            window.Show();
            Telemetry.Event("Widget", $"打开 {kind}");
        }
        else if (_windows.TryGetValue(kind, out var existing))
        {
            existing.Close();
        }
    }

    /// <summary>窗口被关闭后同步开关状态（suppress 防止 OnXxxChanged 递归）。</summary>
    private void SetOpenFlagSilently(WidgetKind kind, bool value)
    {
        _suppressToggle = true;
        try
        {
            switch (kind)
            {
                case WidgetKind.Stats: StatsOpen = value; break;
                case WidgetKind.Date: DateOpen = value; break;
                case WidgetKind.Weather: WeatherOpen = value; break;
                case WidgetKind.Music: MusicOpen = value; break;
                case WidgetKind.Search: SearchOpen = value; break;
            }
        }
        finally
        {
            _suppressToggle = false;
        }
    }

    public void Dispose()
    {
        // ToList 防止 Close 触发 Closed→Remove 导致迭代期修改集合。
        // 退出前置 PersistCloseState=false：Close 窗口让进程正常退出，但不把 IsOpen 覆写回 false，
        // 这样「重启恢复已打开的小组件」仍生效；用户手动点 ✕ 关闭才落盘关闭状态。
        foreach (var window in _windows.Values.ToList())
        {
            window.PersistCloseState = false;
            window.Close();
        }
        _windows.Clear();
        GC.SuppressFinalize(this);
    }
}
