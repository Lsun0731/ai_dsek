using System.Diagnostics;
using System.Runtime.InteropServices;
using AiDesk.Core.SecondDesktop;

namespace AiDesk.App.SecondDesktop;

/// <summary>
/// 第二桌面控制器：进入时隐藏任务栏/桌面图标并显示全屏覆盖层，
/// 退出时恢复并（可选）激活目标应用窗口。
/// </summary>
public sealed class SecondDesktopController
{
    private const uint MOD_CONTROL = 0x2;
    private const uint MOD_ALT = 0x1;
    private const uint VK_D = 0x44;

    private readonly TaskbarHider _taskbar = new();
    private readonly DesktopIconHider _icons = new();
    private SecondDesktopWindow? _window;

    public bool IsActive { get; private set; }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>进入第二桌面（隐藏任务栏/图标 + 显示全屏覆盖层）。任何异常都确保恢复系统 UI。</summary>
    public void Enter()
    {
        if (IsActive)
            return;
        IsActive = true;

        try
        {
            _taskbar.Hide();
            _icons.Hide();
        }
        catch
        {
            // 隐藏失败立即恢复，避免卡在无任务栏状态
            _taskbar.Restore();
            _icons.Restore();
            IsActive = false;
            return;
        }

        try
        {
            _window = new SecondDesktopWindow();
            _window.Closed += (_, _) => Exit(_window.PendingActivateProcessId);
            _window.Show();
        }
        catch
        {
            _taskbar.Restore();
            _icons.Restore();
            IsActive = false;
            throw;
        }
    }

    private void Exit(int? activateProcessId)
    {
        if (!IsActive)
            return;
        IsActive = false;

        try
        {
            _taskbar.Restore();
        }
        finally
        {
            _icons.Restore();
        }
        _window = null;

        if (activateProcessId is int pid)
            ActivateProcess(pid);
    }

    private static void ActivateProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.MainWindowHandle != IntPtr.Zero)
                SetForegroundWindow(process.MainWindowHandle);
        }
        catch
        {
            // 进程已退出则忽略
        }
    }

    /// <summary>注册全局热键（Ctrl+Alt+D）。返回是否成功。</summary>
    public bool RegisterHotKey(global::AiDesk.App.Services.HotKeyService hotKey)
    {
        hotKey.Pressed += Enter;
        return hotKey.Register(MOD_CONTROL | MOD_ALT, VK_D);
    }
}
