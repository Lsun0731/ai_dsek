using System.Runtime.InteropServices;

namespace AiDesk.App.Services;

/// <summary>跨窗口复用的 Win32 原生方法。</summary>
public static class NativeMethods
{
    public const int SW_MINIMIZE = 6;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
