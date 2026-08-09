using System.Diagnostics;
using System.Drawing;
using Microsoft.Win32;

namespace AiDesk.Core.Desktop;

/// <summary>
/// Windows 外观模式服务：深色/浅色模式、任务栏透明度、强调色（注册表 Personalize）。
/// </summary>
public sealed class AppearanceService
{
    private const string PersonalizePath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>设置应用与系统的深浅色模式。</summary>
    /// <param name="dark">true=深色，false=浅色；null=不修改该项。</param>
    public void SetTheme(bool? dark)
    {
        using var key = OpenPersonalize(writable: true);
        if (dark is { } d)
        {
            key.SetValue("AppsUseLightTheme", d ? 0 : 1, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", d ? 0 : 1, RegistryValueKind.DWord);
        }
        RefreshTheme();
    }

    /// <summary>读取当前是否为深色模式（应用主题）。</summary>
    public bool? IsDarkTheme()
    {
        using var key = OpenPersonalize(writable: false);
        var value = key?.GetValue("AppsUseLightTheme");
        return value is int i ? i == 0 : null;
    }

    /// <summary>设置任务栏/系统透明度效果。</summary>
    public void SetTransparency(bool enabled)
    {
        using var key = OpenPersonalize(writable: true);
        key.SetValue("EnableTransparency", enabled ? 1 : 0, RegistryValueKind.DWord);
        RefreshTheme();
    }

    /// <summary>读取透明度效果是否开启。</summary>
    public bool? IsTransparencyEnabled()
    {
        using var key = OpenPersonalize(writable: false);
        var value = key?.GetValue("EnableTransparency");
        return value is int i ? i == 1 : null;
    }

    /// <summary>设置强调色（AccentColor 为 ABGR 格式 DWORD）。</summary>
    public void SetAccentColor(Color color)
    {
        using var key = OpenPersonalize(writable: true);
        key.SetValue("AccentColor", AccentColorConverter.ToDword(color), RegistryValueKind.DWord);
        RefreshTheme();
    }

    /// <summary>读取当前强调色。</summary>
    public Color? GetAccentColor()
    {
        using var key = OpenPersonalize(writable: false);
        var value = key?.GetValue("AccentColor");
        return value is int i ? AccentColorConverter.FromDword(unchecked((uint)i)) : null;
    }

    private static RegistryKey OpenPersonalize(bool writable) =>
        Registry.CurrentUser.OpenSubKey(PersonalizePath, writable)
        ?? throw new InvalidOperationException($"无法打开注册表项：{PersonalizePath}");

    /// <summary>通知系统刷新个性化设置（经典机制，深浅色/透明度改动后立即生效）。</summary>
    private static void RefreshTheme()
    {
        try
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,UpdatePerUserSystemParameters")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch
        {
            // 刷新失败不影响注册表已写入的结果
        }
    }
}
