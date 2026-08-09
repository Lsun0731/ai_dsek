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
    private readonly WidgetSettings _settings = WidgetConfig.Load();

    [ObservableProperty]
    private bool _statsOpen;

    [ObservableProperty]
    private bool _dateOpen;

    [ObservableProperty]
    private bool _weatherOpen;

    [ObservableProperty]
    private double _widgetOpacity = 0.9;

    [ObservableProperty]
    private string _weatherCity = "北京";

    public WidgetViewModel()
    {
        WidgetOpacity = _settings.Opacity;
        WeatherCity = _settings.WeatherCity;
        StatsOpen = _settings.GetState(WidgetKind.Stats).IsOpen;
        DateOpen = _settings.GetState(WidgetKind.Date).IsOpen;
        WeatherOpen = _settings.GetState(WidgetKind.Weather).IsOpen;
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

    partial void OnWidgetOpacityChanged(double value)
    {
        _settings.Opacity = value;
        WidgetConfig.Save(_settings);
        foreach (var window in _windows.Values)
            window.SetWidgetOpacity(value);
    }

    partial void OnWeatherCityChanged(string value)
    {
        _settings.WeatherCity = value;
        WidgetConfig.Save(_settings);
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
            // 持久化打开状态，重启后记住已打开的组件
            _settings.GetState(kind).IsOpen = true;
            WidgetConfig.Save(_settings);
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
            }
        }
        finally
        {
            _suppressToggle = false;
        }
    }

    public void Dispose()
    {
        // 不 Close 窗口：应用退出时窗口随进程销毁，避免 OnClosing 把 IsOpen 覆写回 false
        // （那样「重启恢复已打开的小组件」会失效）；用户手动点 ✕ 关闭才走 OnClosing 落盘关闭状态。
        _windows.Clear();
        GC.SuppressFinalize(this);
    }
}
