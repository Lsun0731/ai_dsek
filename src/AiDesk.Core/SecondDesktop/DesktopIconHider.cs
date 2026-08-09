using System.Runtime.InteropServices;

namespace AiDesk.Core.SecondDesktop;

/// <summary>
/// 隐藏/恢复桌面图标（内存态：隐藏 Progman 下的 SHELLDLL_DefView 图标列表窗口）。
/// 不写注册表 → 应用崩溃/被杀后重启系统即自动恢复，无跨重启残留。
/// </summary>
public sealed class DesktopIconHider
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>当前是否处于隐藏状态。</summary>
    public bool IsHidden { get; private set; }

    /// <summary>找到桌面图标列表窗口（Progman → SHELLDLL_DefView）。找不到返回 0。</summary>
    public IntPtr GetIconListHandle()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            return IntPtr.Zero;
        return FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
    }

    /// <summary>隐藏桌面图标。</summary>
    public bool Hide()
    {
        var hwnd = GetIconListHandle();
        if (hwnd == IntPtr.Zero)
            return false;
        IsHidden = ShowWindow(hwnd, SW_HIDE);
        return IsHidden;
    }

    /// <summary>恢复桌面图标。</summary>
    public bool Restore()
    {
        var hwnd = GetIconListHandle();
        if (hwnd == IntPtr.Zero)
            return false;
        IsHidden = !ShowWindow(hwnd, SW_SHOW);
        return !IsHidden;
    }
}
