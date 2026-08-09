using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Widgets;

/// <summary>
/// 桌面小组件：时钟 + 日期 + CPU/内存监控悬浮窗。
/// 无边框置顶、可拖动、位置持久化。
/// </summary>
public partial class SystemWidgetWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memCounter;
    private readonly WidgetSettings _settings;

    public SystemWidgetWindow()
    {
        InitializeComponent();
        _settings = WidgetConfig.Load();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = _settings.Left;
        Top = _settings.Top;
        Opacity = _settings.Opacity;

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _memCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
            _cpuCounter.NextValue(); // 首次采样为 0，预热
        }
        catch
        {
            // 计数器不可用时监控显示 --%
        }

        _timer.Tick += OnTick;
        _timer.Start();
        OnTick(this, EventArgs.Empty);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        TimeText.Text = DateTime.Now.ToString("HH:mm");
        DateText.Text = DateTime.Now.ToString("yyyy年M月d日 dddd");

        // CPU 每秒更新（计数器本身需要间隔采样）；内存每 2 秒
        try
        {
            if (_cpuCounter is not null)
                CpuText.Text = $"{Math.Clamp(_cpuCounter.NextValue(), 0, 100):F0}%";
            if (_memCounter is not null && DateTime.Now.Second % 2 == 0)
                MemText.Text = $"{Math.Clamp(_memCounter.NextValue(), 0, 100):F0}%";
        }
        catch
        {
            // 计数器异常不干扰时钟
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Telemetry.Event("Widget", "关闭小组件");
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        _cpuCounter?.Dispose();
        _memCounter?.Dispose();

        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Opacity = Opacity;
        _settings.IsWidgetOpen = false;
        WidgetConfig.Save(_settings);
    }
}
