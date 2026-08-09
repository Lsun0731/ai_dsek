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

    // SPI_SETICONS：强制刷新桌面图标（注册表 HideIcons 改动后必须调用才生效）
    private const uint SPI_SETICONS = 0x003A;
    private const uint SPIF_SENDCHANGE = 0x0002;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

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
        // SPI_SETICONS 强制刷新桌面图标（仅 SHChangeNotify 在部分 Win11 上不生效）
        SystemParametersInfo(SPI_SETICONS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    private bool? _originalState;
}
