using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.SecondDesktop;

namespace AiDesk.App.DesktopDock;

/// <summary>磁贴数据（运行中应用窗口）。</summary>
public sealed record DockTileModel
{
    public required string Title { get; init; }
    public required int ProcessId { get; init; }
    public ImageSource? Icon { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// 桌面 Dock（任务栏替代，macOS Dock 风格）：Topmost 悬浮条，显示运行中应用的窗口按钮。
/// 任务栏式行为：点击激活（已激活则最小化）、当前前台窗口高亮。
/// 不挂 WorkerW——悬浮于所有窗口之上，避免桌面图层挂载的渲染/可见性问题。
/// </summary>
public partial class DesktopDockWindow : Window
{
    private readonly RunningAppsProvider _runningApps = new();
    private readonly DispatcherTimer _appsTimer;
    private readonly DispatcherTimer _focusTimer;

    public DesktopDockWindow()
    {
        InitializeComponent();

        // 位置：底部居中
        Top = SystemParameters.PrimaryScreenHeight - 108;
        SizeChanged += (_, _) =>
        {
            if (ActualWidth > 0)
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        };

        RefreshRunningApps();

        // 应用列表刷新（5 秒）
        _appsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _appsTimer.Tick += (_, _) => RefreshRunningApps();
        _appsTimer.Start();

        // 前台窗口高亮刷新（300ms，任务栏行为）
        _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _focusTimer.Tick += (_, _) => RefreshActiveHighlight();
        _focusTimer.Start();

        Closing += OnWindowClosing;
    }

    // ---- 磁贴 ----

    private void RefreshRunningApps()
    {
        try
        {
            var apps = _runningApps.Refresh();
            var foregroundPid = GetForegroundProcessId();
            TilesHost.ItemsSource = apps.Select(a => new DockTileModel
            {
                Title = a.Title,
                ProcessId = a.ProcessId,
                Icon = IconHelper.GetExecutableIcon(a.ExecutablePath),
                IsActive = a.ProcessId == foregroundPid,
            }).ToList();
            Telemetry.Info("Dock", $"刷新磁贴 {apps.Count} 个应用");
        }
        catch (Exception ex)
        {
            Telemetry.Error("Dock.RefreshApps", ex);
        }
    }

    /// <summary>仅更新高亮（不重建列表，避免磁贴闪烁）。</summary>
    private void RefreshActiveHighlight()
    {
        if (TilesHost.ItemsSource is not List<DockTileModel> tiles)
            return;
        var foregroundPid = GetForegroundProcessId();
        var changed = false;
        for (var i = 0; i < tiles.Count; i++)
        {
            var active = tiles[i].ProcessId == foregroundPid;
            if (tiles[i].IsActive != active)
            {
                tiles[i] = tiles[i] with { IsActive = active };
                changed = true;
            }
        }
        if (changed)
            TilesHost.ItemsSource = null;
        if (changed)
            TilesHost.ItemsSource = tiles;
    }

    private static uint GetForegroundProcessId()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return 0;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int processId })
            return;
        ActivateOrMinimize(processId);
    }

    /// <summary>任务栏行为：已在前台则最小化，否则激活。</summary>
    private static void ActivateOrMinimize(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero)
                return;
            if (NativeMethods.GetForegroundWindow() == handle)
                NativeMethods.ShowWindow(handle, NativeMethods.SW_MINIMIZE);
            else
                NativeMethods.SetForegroundWindow(handle);
        }
        catch
        {
            // 进程已退出则忽略
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        _appsTimer.Stop();
        _focusTimer.Stop();
    }
}
