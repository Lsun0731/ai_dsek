namespace AiDesk.Core.Desktop;

/// <summary>桌面壁纸填充方式。</summary>
public enum WallpaperStyle
{
    /// <summary>填充（等比放大裁剪，Win7+）</summary>
    Fill,

    /// <summary>适应（等比缩放留边，Win7+）</summary>
    Fit,

    /// <summary>拉伸（变形铺满）</summary>
    Stretch,

    /// <summary>平铺</summary>
    Tile,

    /// <summary>居中</summary>
    Center,

    /// <summary>跨显示器（Win8+）</summary>
    Span,
}

/// <summary>
/// 壁纸填充方式的注册表映射（HKCU\Control Panel\Desktop）。
/// WallpaperStyle 与 TileWallpaper 的组合决定填充方式（纯函数，可单测）。
/// </summary>
public static class WallpaperStyleMapper
{
    /// <summary>返回 (WallpaperStyle 注册表值, TileWallpaper 注册表值)。</summary>
    public static (string WallpaperStyle, string TileWallpaper) ToRegistryValues(WallpaperStyle style) => style switch
    {
        WallpaperStyle.Fill => ("10", "0"),
        WallpaperStyle.Fit => ("6", "0"),
        WallpaperStyle.Stretch => ("2", "0"),
        WallpaperStyle.Tile => ("0", "1"),
        WallpaperStyle.Center => ("0", "0"),
        WallpaperStyle.Span => ("22", "0"),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "未知的壁纸填充方式"),
    };

    public static string DisplayName(WallpaperStyle style) => style switch
    {
        WallpaperStyle.Fill => "填充",
        WallpaperStyle.Fit => "适应",
        WallpaperStyle.Stretch => "拉伸",
        WallpaperStyle.Tile => "平铺",
        WallpaperStyle.Center => "居中",
        WallpaperStyle.Span => "跨区",
        _ => style.ToString(),
    };
}
