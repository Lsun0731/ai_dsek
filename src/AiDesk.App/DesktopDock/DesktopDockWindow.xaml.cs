using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.SecondDesktop;

namespace AiDesk.App.DesktopDock;

/// <summary>磁贴数据（运行中应用）。</summary>
public sealed record DockTileModel
{
    public required string Title { get; init; }
    public required int ProcessId { get; init; }
    public ImageSource? Icon { get; init; }
}

/// <summary>
/// 桌面 Dock（任务栏替代）：挂载到桌面图层，显示运行中应用磁贴，点击激活/切换。
/// 音乐监控与搜索已拆分为独立小组件（见 Widgets 模块）。
/// </summary>
public partial class DesktopDockWindow : Window
{
    private readonly RunningAppsProvider _runningApps = new();
    private readonly DispatcherTimer _appsTimer;

    /// <summary>挂载失败降级标志（挂到普通置顶窗口）。</summary>
    public bool AttachedToDesktop { get; private set; }

    public DesktopDockWindow()
    {
        InitializeComponent();

        // 位置：底部居中（内容自适应宽度，SizeChanged 时重新居中）
        Top = SystemParameters.PrimaryScreenHeight - 96;
        SizeChanged += (_, _) =>
        {
            if (ActualWidth > 0)
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        };

        RefreshRunningApps();

        _appsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _appsTimer.Tick += (_, _) => RefreshRunningApps();
        _appsTimer.Start();

        Closing += OnWindowClosing;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>窗口句柄就绪后挂载到桌面图层；失败则降级为普通置顶窗口。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            AttachedToDesktop = DesktopLayerHost.AttachToDesktop(hwnd);
            if (!AttachedToDesktop)
            {
                Topmost = true; // 降级：无法挂桌面层时置顶显示
                Telemetry.Info("Dock", "桌面图层挂载失败，降级为置顶窗口");
            }
        }
        catch (Exception ex)
        {
            AttachedToDesktop = false;
            Topmost = true;
            Telemetry.Error("Dock.Attach", ex);
        }
    }

    // ---- 磁贴 ----

    private void RefreshRunningApps()
    {
        try
        {
            var apps = _runningApps.Refresh();
            TilesHost.ItemsSource = apps.Select(a => new DockTileModel
            {
                Title = a.Title,
                ProcessId = a.ProcessId,
                Icon = IconHelper.GetExecutableIcon(a.ExecutablePath),
            }).ToList();
            Telemetry.Info("Dock", $"刷新磁贴 {apps.Count} 个应用");
        }
        catch (Exception ex)
        {
            Telemetry.Error("Dock.RefreshApps", ex);
        }
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int processId })
            ActivateProcess(processId);
    }

    private static void ActivateProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.MainWindowHandle != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(process.MainWindowHandle);
        }
        catch
        {
            // 进程已退出则忽略
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        _appsTimer.Stop();
    }
}
