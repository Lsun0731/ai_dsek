using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AiDesk.Core.Desktop;

/// <summary>
/// 桌面壁纸服务：设置壁纸、读取当前壁纸、定时轮播。
/// </summary>
public sealed class WallpaperService : IDisposable
{
    private const string DesktopRegistryPath = @"Control Panel\Desktop";

    private System.Timers.Timer? _timer;
    private string[] _slidePaths = [];
    private int _slideIndex = -1;
    private readonly Random _random = new();

    private bool _disposed;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    /// <summary>设置壁纸并应用填充方式。</summary>
    /// <param name="imagePath">图片文件路径。</param>
    /// <param name="style">填充方式。</param>
    public void SetWallpaper(string imagePath, WallpaperStyle style)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("壁纸路径不能为空", nameof(imagePath));
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"壁纸文件不存在：{imagePath}", imagePath);

        var (wallpaperStyle, tileWallpaper) = WallpaperStyleMapper.ToRegistryValues(style);
        using var key = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 HKCU\\Control Panel\\Desktop");
        key.SetValue("WallpaperStyle", wallpaperStyle, RegistryValueKind.String);
        key.SetValue("TileWallpaper", tileWallpaper, RegistryValueKind.String);

        if (!SystemParametersInfo(SpiSetDeskWallpaper, 0, imagePath,
                SpifUpdateIniFile | SpifSendChange))
        {
            throw new InvalidOperationException($"设置壁纸失败（Win32 错误码 {Marshal.GetLastWin32Error()}）");
        }
    }

    /// <summary>读取当前壁纸路径（注册表，可能为空）。</summary>
    public string? GetCurrentWallpaper()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath);
        return key?.GetValue("Wallpaper") as string;
    }

    /// <summary>
    /// 启动壁纸轮播：按固定间隔在图片列表中轮换。
    /// </summary>
    /// <param name="imagePaths">候选图片列表。</param>
    /// <param name="interval">切换间隔。</param>
    /// <param name="style">填充方式。</param>
    /// <param name="shuffle">是否随机顺序（顺序模式在列表内循环）。</param>
    /// <param name="applyImmediately">是否立即切换一张。</param>
    public void StartSlideshow(IReadOnlyList<string> imagePaths, TimeSpan interval, WallpaperStyle style, bool shuffle, bool applyImmediately)
    {
        if (imagePaths.Count == 0)
            throw new ArgumentException("轮播图片列表不能为空", nameof(imagePaths));
        if (interval <= TimeSpan.Zero)
            throw new ArgumentException("轮播间隔必须大于 0", nameof(interval));

        _slidePaths = shuffle
            ? imagePaths.OrderBy(_ => _random.Next()).ToArray()
            : imagePaths.ToArray();
        _slideIndex = -1;

        _timer?.Dispose();
        _timer = new System.Timers.Timer(interval.TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += (_, _) => NextWallpaper(style);
        _timer.Start();

        if (applyImmediately)
            NextWallpaper(style);
    }

    /// <summary>停止壁纸轮播。</summary>
    public void StopSlideshow()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public bool IsSlideshowRunning => _timer is not null;

    /// <summary>立即切换到下一张壁纸（无轮播时也可手动切换）。</summary>
    public void NextWallpaper(WallpaperStyle style)
    {
        if (_slidePaths.Length == 0)
            return;

        _slideIndex = (_slideIndex + 1) % _slidePaths.Length;
        SetWallpaper(_slidePaths[_slideIndex], style);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        GC.SuppressFinalize(this);
    }
}
