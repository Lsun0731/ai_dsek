using System.Runtime.InteropServices;

namespace AiDesk.Core.SecondDesktop;

/// <summary>
/// 把窗口挂载到 Windows 桌面图层（壁纸之上、桌面图标之下、应用窗口之下），
/// 使窗口成为"桌面的一部分"——这是动态壁纸/桌面增强元素的经典做法（WorkerW 技术）。
/// </summary>
public static class DesktopLayerHost
{
    private const int WM_SPAWN_WORKERW = 0x052C;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>把指定窗口挂到桌面图层。挂载后窗口将随桌面显示（壁纸之上、图标之下）。</summary>
    public static bool AttachToDesktop(IntPtr windowHandle)
    {
        var workerW = FindDesktopWorkerW();
        var parent = workerW != IntPtr.Zero ? workerW : FindWindow("Progman", null);
        if (parent == IntPtr.Zero)
            return false;

        if (SetParent(windowHandle, parent) == IntPtr.Zero)
            return false;

        // 置于 Z 序最底（桌面层）
        SetWindowPos(windowHandle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        return true;
    }

    /// <summary>查找桌面 WorkerW 窗口（动态壁纸宿主）。找不到返回 0。</summary>
    public static IntPtr FindDesktopWorkerW()
    {
        // 触发 Progman 创建 WorkerW（仅首次需要）
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
            SendMessage(progman, WM_SPAWN_WORKERW, new IntPtr(0xD), IntPtr.Zero);

        // 找到带 SHELLDLL_DefView 子窗口的 WorkerW（桌面图标的宿主层）
        IntPtr workerW = IntPtr.Zero;
        while ((workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null)) != IntPtr.Zero)
        {
            var defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
                return workerW;
        }
        return IntPtr.Zero;
    }
}
