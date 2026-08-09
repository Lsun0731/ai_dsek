using AiDesk.App.DesktopDock;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.SecondDesktop;

namespace AiDesk.App.DesktopDock;

/// <summary>
/// 桌面集成控制器：管理桌面 Dock 生命周期 + 任务栏/图标隐藏开关。
/// Dock 挂载到桌面图层（壁纸之上、应用之下），常驻桌面替代任务栏。
/// </summary>
public sealed class DesktopIntegrationController
{
    private static DesktopIntegrationController? _instance;

    public static DesktopIntegrationController? Instance => _instance;

    /// <summary>应用启动时初始化（只调用一次）。</summary>
    public static void Initialize()
    {
        _instance = new DesktopIntegrationController();
        _instance.ApplySettings();
    }

    private readonly TaskbarHider _taskbar = new();
    private readonly DesktopIconHider _icons = new();
    private DesktopDockWindow? _dock;
    private AppSettings _settings;

    /// <summary>Dock 预留的底部工作区高度（Dock 高 108 + 边距）。</summary>
    private const int DockReserveHeight = 116;

    private DesktopIntegrationController()
    {
        _settings = AppConfig.Load();
    }

    public AppSettings Settings => _settings;

    /// <summary>按当前配置应用：创建/销毁 Dock + 任务栏/图标隐藏状态 + 工作区预留。</summary>
    public void ApplySettings()
    {
        if (_settings.Dock.Enabled && _dock is null)
        {
            _dock = new DesktopDockWindow();
            _dock.Closed += (_, _) => _dock = null;
            _dock.Show();
        }
        else if (!_settings.Dock.Enabled && _dock is not null)
        {
            _dock.Close();
            _dock = null;
        }

        // 工作区预留：Dock 启用且隐藏任务栏时，把工作区底部让给 Dock（应用最大化不覆盖 Dock，Dock 也不挡应用）
        ReserveDockArea(_settings.Dock.Enabled && _settings.Dock.HideTaskbar);

        SetTaskbarHidden(_settings.Dock.HideTaskbar);
        SetIconsHidden(_settings.Dock.HideIcons);
    }

    /// <summary>预留/恢复底部工作区（SPI_SETWORKAREA）。隐藏任务栏时原工作区=全屏，可安全预留。</summary>
    private void ReserveDockArea(bool reserve)
    {
        try
        {
            var rect = new NativeMethods.RECT
            {
                Right = NativeMethods.GetSystemMetrics(0), // SM_CXSCREEN
                Bottom = reserve
                    ? NativeMethods.GetSystemMetrics(1) - DockReserveHeight // SM_CYSCREEN
                    : NativeMethods.GetSystemMetrics(1),
            };
            NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_SETWORKAREA, 0, ref rect, NativeMethods.SPIF_SENDCHANGE);
        }
        catch (Exception ex)
        {
            Telemetry.Error("Dock.WorkArea", ex);
        }
    }

    /// <summary>保存设置并立即应用。</summary>
    public void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        AppConfig.Save(settings);
        ApplySettings();
    }

    private void SetTaskbarHidden(bool hide)
    {
        try
        {
            if (hide) _taskbar.Hide();
            else _taskbar.Restore();
        }
        catch (Exception ex)
        {
            Telemetry.Error("Dock.Taskbar", ex);
        }
    }

    private void SetIconsHidden(bool hide)
    {
        try
        {
            if (hide) _icons.Hide();
            else _icons.Restore();
        }
        catch (Exception ex)
        {
            Telemetry.Error("Dock.Icons", ex);
        }
    }

    /// <summary>应用退出时恢复系统 UI（explorer 独立进程，隐藏状态/工作区会残留）。</summary>
    public void Cleanup()
    {
        try { _taskbar.Restore(); } catch { }
        try { _icons.Restore(); } catch { }
        ReserveDockArea(false);
        _dock?.Close();
    }
}
