using System.Runtime.InteropServices;

namespace AiDesk.App.Services;

/// <summary>跨窗口复用的 Win32 原生方法。</summary>
public static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
