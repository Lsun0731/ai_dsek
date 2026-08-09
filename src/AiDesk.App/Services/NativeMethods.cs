using System.Runtime.InteropServices;

namespace AiDesk.App.Services;

/// <summary>跨窗口复用的 Win32 原生方法。</summary>
public static class NativeMethods
{
    public const int SW_MINIMIZE = 6;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const uint VK_MENU = 0x12;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public const uint SPI_SETWORKAREA = 0x002F;
    public const uint SPIF_SENDCHANGE = 0x0002;

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    /// <summary>设为工具窗口样式：不出现在 Alt+Tab 切换列表。</summary>
    public static void SetToolWindow(IntPtr hWnd)
    {
        var exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_TOOLWINDOW;
        exStyle &= ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    /// <summary>激活窗口。模拟 Alt 键按下以解除前台锁定（后台进程 SetForegroundWindow 会被系统拒绝）。</summary>
    public static void ActivateWindow(IntPtr hWnd)
    {
        keybd_event((byte)VK_MENU, 0, 0, UIntPtr.Zero);
        SetForegroundWindow(hWnd);
        keybd_event((byte)VK_MENU, 0, 2 /*KEYEVENTF_KEYUP*/, UIntPtr.Zero);
    }
}
