using System.Windows;
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

    /// <summary>Dock 高度（DIP）。预留工作区时按 DPI 换算为物理像素。</summary>
    private const double DockHeightDip = 108;

    private bool _workAreaReserved;
    private NativeMethods.RECT _originalWorkArea;

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

    /// <summary>预留/恢复底部工作区（SPI_SETWORKAREA）。只在曾预留过时恢复原始值，避免覆盖任务栏区域。</summary>
    private void ReserveDockArea(bool reserve)
    {
        try
        {
            if (reserve && !_workAreaReserved)
            {
                // 保存原始工作区，恢复时还原
                NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_GETWORKAREA, 0, ref _originalWorkArea, 0);
                // 预留高度 = Dock 高（DIP）× DPI 比例 + 边距（物理像素）
                var dpiScale = (double)NativeMethods.GetSystemMetrics(1) / SystemParameters.PrimaryScreenHeight;
                var reservePx = (int)Math.Round((DockHeightDip + 8) * dpiScale);
                var rect = new NativeMethods.RECT
                {
                    Right = NativeMethods.GetSystemMetrics(0), // SM_CXSCREEN（物理像素）
                    Bottom = NativeMethods.GetSystemMetrics(1) - reservePx,
                };
                NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_SETWORKAREA, 0, ref rect, NativeMethods.SPIF_SENDCHANGE);
                _workAreaReserved = true;
            }
            else if (!reserve && _workAreaReserved)
            {
                NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_SETWORKAREA, 0, ref _originalWorkArea, NativeMethods.SPIF_SENDCHANGE);
                _workAreaReserved = false;
            }
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
