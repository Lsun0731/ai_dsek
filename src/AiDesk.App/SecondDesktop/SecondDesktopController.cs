using System.Diagnostics;
using System.Runtime.InteropServices;
using AiDesk.Core.SecondDesktop;

namespace AiDesk.App.SecondDesktop;

/// <summary>
/// 第二桌面控制器（模式状态机）。
///
/// 模式 = 任务栏隐藏 + 桌面图标隐藏（"第二桌面环境"）。
/// 启动器（全屏覆盖层）是模式内的入口界面：
///   - 点磁贴/启动应用 → 隐藏启动器，应用在前台显示（模式保持，任务栏仍隐藏）
///   - 再按热键 → 启动器重新呼出，可切换其他应用
///   - 启动器内 Esc/返回 → 退出模式，恢复任务栏/图标
/// </summary>
public sealed class SecondDesktopController
{
    private const uint MOD_CONTROL = 0x2;
    private const uint MOD_ALT = 0x1;
    private const uint VK_D = 0x44;

    private readonly TaskbarHider _taskbar = new();
    private readonly DesktopIconHider _icons = new();
    private SecondDesktopWindow? _window;

    /// <summary>是否处于第二桌面模式（任务栏/图标隐藏中）。</summary>
    public bool IsModeActive { get; private set; }

    /// <summary>启动器当前是否可见。</summary>
    public bool IsLauncherVisible => _window?.IsVisible ?? false;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 热键 / 托盘图标统一入口：
    /// 未进入模式 → 进入；模式中启动器可见 → 隐藏启动器（继续用当前应用）；启动器隐藏 → 呼出。
    /// </summary>
    public void ToggleOrEnter()
    {
        if (!IsModeActive)
        {
            Enter();
            return;
        }
        if (IsLauncherVisible)
        {
            _window?.Hide();
        }
        else if (_window is not null)
        {
            _window.Show();
            _window.Activate();
        }
    }

    /// <summary>进入第二桌面模式：隐藏任务栏/图标并显示启动器。异常时恢复系统 UI。</summary>
    public void Enter()
    {
        if (IsModeActive)
        {
            if (!IsLauncherVisible)
            {
                _window?.Show();
                _window?.Activate();
            }
            return;
        }
        IsModeActive = true;

        try
        {
            _taskbar.Hide();
            _icons.Hide();
        }
        catch
        {
            _taskbar.Restore();
            _icons.Restore();
            IsModeActive = false;
            return;
        }

        try
        {
            _window ??= CreateLauncher();
            _window.Show();
            _window.Activate();
        }
        catch
        {
            _taskbar.Restore();
            _icons.Restore();
            IsModeActive = false;
            throw;
        }
    }

    /// <summary>退出第二桌面模式：恢复任务栏/图标并关闭启动器。</summary>
    public void Exit()
    {
        if (!IsModeActive)
            return;
        IsModeActive = false;

        try
        {
            _taskbar.Restore();
        }
        finally
        {
            _icons.Restore();
        }

        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }
    }

    private SecondDesktopWindow CreateLauncher()
    {
        var window = new SecondDesktopWindow();
        // 点磁贴：隐藏启动器 + 激活目标应用（留在第二桌面模式）
        window.LaunchRequested += processId =>
        {
            window.Hide();
            ActivateProcess(processId);
        };
        // Esc/返回：退出整个第二桌面模式
        window.ExitRequested += Exit;
        return window;
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
        hotKey.Pressed += ToggleOrEnter;
        return hotKey.Register(MOD_CONTROL | MOD_ALT, VK_D);
    }
}
