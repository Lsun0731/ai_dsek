using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AiDesk.Core.Cursors;

/// <summary>
/// 光标主题服务：枚举系统已安装的光标方案并应用（官方注册表机制，安全可逆）。
/// 自定义方案定义在 HKCU\Control Panel\Cursors\Schemes（值名=方案名，值=逗号分隔的光标路径）；
/// 未自定义方案的机器上该键不存在，此时仅支持「恢复默认」。
/// 应用时写入 HKCU\Control Panel\Cursors 各光标值并广播 SPI_SETCURSORS。
/// </summary>
public sealed class CursorThemeService
{
    private const string CursorsPath = @"Control Panel\Cursors";
    private const string SchemesPath = @"Control Panel\Cursors\Schemes";

    private const uint SpiSetCursors = 0x0057;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    /// <summary>Windows 各版本出现过的光标值名（应用方案时只覆盖实际存在的值）。</summary>
    private static readonly string[] CursorValueNames =
    [
        "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "No",
        "Pen", "NWPen", "SizeNWSE", "SizeNESW", "SizeWE", "SizeNS", "SizeAll",
        "UpArrow", "Hand", "Pin", "Link", "Person", "AlternativeCursor1",
        "AlternativeCursor2", "AlternativeCursor3", "AlternativeCursor4",
    ];

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    /// <summary>枚举系统已安装的自定义光标方案名（无自定义方案时返回空列表）。</summary>
    public IReadOnlyList<string> GetSchemes()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SchemesPath);
        return key is null
            ? []
            : key.GetValueNames().Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => n).ToList();
    }

    /// <summary>读取当前应用的光标方案名。</summary>
    public string? GetCurrentScheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorsPath);
        var value = key?.GetValue(null) as string;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>应用指定方案。方案不存在返回 false。</summary>
    public bool ApplyScheme(string schemeName)
    {
        using var schemesKey = Registry.CurrentUser.OpenSubKey(SchemesPath);
        var schemeValue = schemesKey?.GetValue(schemeName) as string;
        if (string.IsNullOrWhiteSpace(schemeValue))
            return false;

        var fields = schemeValue.Split(',');
        ApplyFields(fields, schemeName);
        return true;
    }

    /// <summary>恢复 Windows 默认光标（清空自定义光标值后广播刷新）。</summary>
    public void RestoreDefault()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorsPath, writable: true)
            ?? throw new InvalidOperationException($"无法打开注册表项：{CursorsPath}");

        // 只清空已知的光标值名（保留 CursorBaseSize/ContactVisualization 等非光标设置）
        foreach (var name in CursorValueNames)
        {
            if (key.GetValue(name) is not null)
                key.DeleteValue(name);
        }
        key.SetValue(null, "Windows 默认", RegistryValueKind.String);

        SystemParametersInfo(SpiSetCursors, 0, null, SpifUpdateIniFile | SpifSendChange);
    }

    /// <summary>把光标路径字段写入 Cursors 键并广播刷新。</summary>
    private static void ApplyFields(string[] fields, string schemeName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorsPath, writable: true)
            ?? throw new InvalidOperationException($"无法打开注册表项：{CursorsPath}");

        for (var i = 0; i < CursorValueNames.Length; i++)
        {
            var path = fields.Length > i ? fields[i].Trim() : string.Empty;
            // 空路径 = 使用系统默认，写入空值即可；值为空串时删除（等同默认）
            if (path.Length == 0)
            {
                if (key.GetValue(CursorValueNames[i]) is not null)
                    key.DeleteValue(CursorValueNames[i]);
            }
            else
            {
                key.SetValue(CursorValueNames[i], path, RegistryValueKind.String);
            }
        }
        key.SetValue(null, schemeName, RegistryValueKind.String);

        SystemParametersInfo(SpiSetCursors, 0, null, SpifUpdateIniFile | SpifSendChange);
    }
}
