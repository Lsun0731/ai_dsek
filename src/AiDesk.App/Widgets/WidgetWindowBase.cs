using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 小组件窗口基类：无边框透明置顶、半透明深色圆角卡片、可拖动、位置/开关持久化、定时刷新。
/// </summary>
public abstract class WidgetWindowBase : Window
{
    // 窗口扩展样式：工具窗口（不进 Alt+Tab），与 WS_EX_APPWINDOW 互斥
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    private readonly WidgetKind _kind;
    private readonly WidgetState _state;
    private readonly WidgetSettings _settings;
    private DispatcherTimer? _ticker;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

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

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnDrag;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>窗口句柄就绪后，设为工具窗口样式：不出现在 Alt+Tab 切换列表。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
            // 通知系统样式已变更（SWP_NOSIZE|SWP_NOMOVE|SWP_FRAMECHANGED）
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                (uint)(0x0001 | 0x0002 | 0x0020));
        }
        catch (Exception ex)
        {
            Telemetry.Error("Widget." + _kind + ".WindowStyle", ex);
        }
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
        // 重新加载磁盘最新配置：只更新本小组件的位置/开关，避免用构造时的旧快照覆写全局设置（透明度/城市）
        var settings = WidgetConfig.Load();
        var state = settings.GetState(_kind);
        state.Left = Left;
        state.Top = Top;
        state.IsOpen = false;
        WidgetConfig.Save(settings);
        Telemetry.Event("Widget", $"关闭 {_kind}");
    }
}
