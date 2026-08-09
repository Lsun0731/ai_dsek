using System.Runtime.InteropServices;

namespace AiDesk.Core.SecondDesktop;

/// <summary>
/// 隐藏/恢复 Windows 任务栏（Shell_TrayWnd）。进入第二桌面时隐藏，返回时恢复。
/// </summary>
public sealed class TaskbarHider
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>任务栏窗口句柄（Win11 同样为 Shell_TrayWnd）。找不到返回 0。</summary>
    public IntPtr GetTaskbarHandle() => FindWindow("Shell_TrayWnd", null);

    /// <summary>当前是否处于隐藏状态。</summary>
    public bool IsHidden { get; private set; }

    /// <summary>隐藏任务栏。</summary>
    public bool Hide()
    {
        var hwnd = GetTaskbarHandle();
        if (hwnd == IntPtr.Zero)
            return false;
        IsHidden = ShowWindow(hwnd, SW_HIDE);
        return IsHidden;
    }

    /// <summary>恢复任务栏。</summary>
    public bool Restore()
    {
        var hwnd = GetTaskbarHandle();
        if (hwnd == IntPtr.Zero)
            return false;
        IsHidden = !ShowWindow(hwnd, SW_SHOW);
        return !IsHidden;
    }
}
