using System.Drawing;

namespace AiDesk.Core.Desktop;

/// <summary>
/// Windows 强调色（AccentColor）转换。
/// 注册表 AccentColor 为 DWORD，格式 0x00BBGGRR（高字节恒为 0，其次 Blue、Green、Red），
/// 与 .NET <see cref="Color"/>（AARRGGBB）字节序相反（纯函数，可单测）。
/// </summary>
public static class AccentColorConverter
{
    /// <summary>转注册表 DWORD（0x00BBGGRR，alpha 字节恒为 0）。</summary>
    public static uint ToDword(Color color) =>
        ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;

    /// <summary>从注册表 DWORD 还原颜色（alpha 固定为不透明）。</summary>
    public static Color FromDword(uint dword) =>
        Color.FromArgb(
            255,
            (int)(dword & 0xFF),
            (int)((dword >> 8) & 0xFF),
            (int)((dword >> 16) & 0xFF));
}
