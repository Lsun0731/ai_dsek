using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 小组件窗口基类：无边框透明置顶、半透明深色圆角卡片、可拖动、位置/开关持久化、定时刷新。
/// </summary>
public abstract class WidgetWindowBase : Window
{
    private readonly WidgetKind _kind;
    private readonly WidgetState _state;
    private readonly WidgetSettings _settings;
    private DispatcherTimer? _ticker;

    protected WidgetWindowBase(WidgetKind kind)
    {
        _kind = kind;
        _settings = WidgetConfig.Load();
        _state = _settings.GetState(kind);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Opacity = _settings.Opacity;

        MouseLeftButtonDown += OnDrag;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>子类实现：定时刷新内容（首次加载也会调用）。</summary>
    protected abstract void OnTick();

    /// <summary>子类可选：窗口加载完成后初始化。</summary>
    protected virtual void OnWidgetLoaded() { }

    /// <summary>启动定时刷新。</summary>
    protected void StartTicker(int intervalSeconds)
    {
        _ticker?.Stop();
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
        _ticker.Tick += (_, _) => SafeTick();
        _ticker.Start();
    }

    private void SafeTick()
    {
        try
        {
            OnTick();
        }
        catch (Exception ex)
        {
            Telemetry.Error("Widget." + _kind, ex);
            _ticker?.Stop();
        }
    }

    /// <summary>外部设置透明度（全局滑块）。</summary>
    public void SetWidgetOpacity(double opacity) => Opacity = opacity;

    /// <summary>外部触发刷新（如天气城市变更）。</summary>
    public void RefreshNow()
    {
        try
        {
            OnTick();
        }
        catch (Exception ex)
        {
            Telemetry.Error("Widget." + _kind, ex);
        }
    }

    /// <summary>小组件右上角 ✕ 关闭按钮（子类 XAML 引用）。</summary>
    protected void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = _state.Left;
        Top = _state.Top;
        OnWidgetLoaded();
        SafeTick();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _ticker?.Stop();
        _state.Left = Left;
        _state.Top = Top;
        _state.IsOpen = false;
        _settings.Widgets[_kind.ToString()] = _state;
        WidgetConfig.Save(_settings);
        Telemetry.Event("Widget", $"关闭 {_kind}");
    }
}
