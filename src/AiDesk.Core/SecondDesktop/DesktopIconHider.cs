using Microsoft.Win32;

namespace AiDesk.Core.SecondDesktop;

/// <summary>
/// 隐藏/恢复桌面图标（HKCU\...\Explorer\Advanced\HideIcons + SHChangeNotify 刷新）。
/// 注意：不要用隐藏 SHELLDLL_DefView 窗口的内存态方案——在部分 Win11 版本上会导致桌面变灰/壁纸消失。
/// 应用退出时须调用 Restore（见 DesktopIntegrationController.Cleanup）。
/// </summary>
public sealed class DesktopIconHider
{
    private const string ExplorerAdvancedPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private const string HideIconsValue = "HideIcons";

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>当前注册表中的 HideIcons 状态（null = 键不存在，等同 0）。</summary>
    public bool? GetHideIconsState()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedPath);
        var value = key?.GetValue(HideIconsValue);
        return value switch
        {
            null => null,
            int i => i != 0,
            _ => null,
        };
    }

    /// <summary>隐藏桌面图标（保存原值用于恢复）。</summary>
    public void Hide()
    {
        _originalState = GetHideIconsState();
        SetHideIcons(true);
    }

    /// <summary>恢复桌面图标（回到进入前的状态）。</summary>
    public void Restore()
    {
        SetHideIcons(_originalState ?? false);
        _originalState = null;
    }

    private static void SetHideIcons(bool hide)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ExplorerAdvancedPath);
        key.SetValue(HideIconsValue, hide ? 1 : 0, RegistryValueKind.DWord);
        // 广播刷新，让资源管理器立即应用（SHCNE_ASSOCCHANGED 对 HideIcons 生效）
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    private bool? _originalState;
}
