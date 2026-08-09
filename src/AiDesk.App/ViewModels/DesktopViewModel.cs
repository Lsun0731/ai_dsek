using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using AiDesk.Core.Desktop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AiDesk.App.ViewModels;

/// <summary>桌面美化页 ViewModel：壁纸 / 轮播 / 外观模式。</summary>
public partial class DesktopViewModel : ObservableObject, IDisposable
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"];

    private readonly WallpaperService _wallpaper = new();
    private readonly AppearanceService _appearance = new();

    // —— 壁纸 ——
    [ObservableProperty]
    private string? _wallpaperPath;

    [ObservableProperty]
    private string _wallpaperStatus = string.Empty;

    [ObservableProperty]
    private StyleOption? _selectedStyle;

    public IReadOnlyList<StyleOption> Styles { get; } =
        Enum.GetValues<WallpaperStyle>()
            .Select(s => new StyleOption(s, WallpaperStyleMapper.DisplayName(s)))
            .ToList();

    // —— 轮播 ——
    [ObservableProperty]
    private bool _slideshowEnabled;

    [ObservableProperty]
    private int _slideshowIntervalMinutes = 30;

    [ObservableProperty]
    private bool _slideshowShuffle = true;

    [ObservableProperty]
    private string _slideshowFolder = string.Empty;

    [ObservableProperty]
    private string _slideshowStatus = string.Empty;

    // —— 外观 ——
    [ObservableProperty]
    private bool _darkMode;

    [ObservableProperty]
    private bool _transparencyEnabled;

    [ObservableProperty]
    private Color _accentColor = Color.FromRgb(0x4C, 0x7C, 0xF3);

    [ObservableProperty]
    private string _appearanceStatus = string.Empty;

    public IReadOnlyList<Color> PresetColors { get; } =
    [
        Color.FromRgb(0x4C, 0x7C, 0xF3), // 蓝
        Color.FromRgb(0xE5, 0x48, 0x4D), // 红
        Color.FromRgb(0xF7, 0x6B, 0x15), // 橙
        Color.FromRgb(0xF5, 0xA6, 0x23), // 黄
        Color.FromRgb(0x30, 0xA4, 0x6C), // 绿
        Color.FromRgb(0x12, 0xA5, 0x94), // 青
        Color.FromRgb(0x8E, 0x4E, 0xC6), // 紫
        Color.FromRgb(0xE9, 0x3D, 0x82), // 粉
        Color.FromRgb(0x11, 0x18, 0x1C), // 黑
    ];

    public DesktopViewModel()
    {
        SelectedStyle = Styles.First(s => s.Value == WallpaperStyle.Fill);
        _wallpaper.SlideshowError += OnSlideshowError;
        try
        {
            _wallpaperPath = _wallpaper.GetCurrentWallpaper();
            _darkMode = _appearance.IsDarkTheme() ?? false;
            _transparencyEnabled = _appearance.IsTransparencyEnabled() ?? false;
            if (_appearance.GetAccentColor() is { } accent)
                _accentColor = ToMediaColor(accent);
        }
        catch
        {
            // 读取失败不阻塞页面
        }
    }

    // —— 壁纸命令 ——

    [RelayCommand]
    private void SelectWallpaper()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择壁纸图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|所有文件|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            WallpaperPath = dialog.FileName;
            WallpaperStatus = string.Empty;
        }
    }

    [RelayCommand]
    private void ApplyWallpaper()
    {
        if (string.IsNullOrWhiteSpace(WallpaperPath))
        {
            WallpaperStatus = "请先选择一张图片";
            return;
        }
        try
        {
            _wallpaper.SetWallpaper(WallpaperPath, SelectedStyle?.Value ?? WallpaperStyle.Fill);
            WallpaperStatus = $"已应用：{Path.GetFileName(WallpaperPath)}";
        }
        catch (Exception ex)
        {
            WallpaperStatus = $"应用失败：{ex.Message}";
        }
    }

    // —— 轮播命令 ——

    [RelayCommand]
    private void SelectSlideshowFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择壁纸文件夹",
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true)
            SlideshowFolder = dialog.FolderName;
    }

    private bool _suppressSlideshowToggle;

    /// <summary>设置开关而不触发 OnSlideshowEnabledChanged（避免失败回滚/错误回调的递归）。</summary>
    private void SetSlideshowEnabledSilently(bool value)
    {
        _suppressSlideshowToggle = true;
        try
        {
            SlideshowEnabled = value;
        }
        finally
        {
            _suppressSlideshowToggle = false;
        }
    }

    partial void OnSlideshowEnabledChanged(bool value)
    {
        if (_suppressSlideshowToggle)
            return;
        if (value)
            StartSlideshow();
        else
            StopSlideshowCore();
    }

    /// <summary>轮播线程出错回调：切回 UI 线程展示错误并关闭开关。</summary>
    private void OnSlideshowError(string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            SlideshowStatus = $"轮播已停止：{message}";
            SetSlideshowEnabledSilently(false);
        });
    }

    private void StartSlideshow()
    {
        // 防重入：已运行（如「立即切换」已启动）只更新状态
        if (_wallpaper.IsSlideshowRunning)
        {
            SlideshowStatus = $"轮播中：每 {SlideshowIntervalMinutes} 分钟切换";
            return;
        }

        var images = ScanImages(SlideshowFolder);
        if (images.Count == 0)
        {
            SlideshowStatus = "文件夹中没有找到图片，请选择包含图片的文件夹";
            SetSlideshowEnabledSilently(false);
            return;
        }
        try
        {
            _wallpaper.StartSlideshow(images, TimeSpan.FromMinutes(SlideshowIntervalMinutes),
                SelectedStyle?.Value ?? WallpaperStyle.Fill, SlideshowShuffle, applyImmediately: true);
            SlideshowStatus = $"轮播中：{images.Count} 张图片，每 {SlideshowIntervalMinutes} 分钟切换";
        }
        catch (Exception ex)
        {
            SlideshowStatus = $"启动失败：{ex.Message}";
            SetSlideshowEnabledSilently(false);
        }
    }

    /// <summary>纯停止（不写状态消息，避免覆盖启动失败/错误提示）。</summary>
    private void StopSlideshowCore()
    {
        _wallpaper.StopSlideshow();
    }

    [RelayCommand]
    private void NextWallpaper()
    {
        try
        {
            if (SlideshowEnabled)
            {
                _wallpaper.NextWallpaper(SelectedStyle?.Value ?? WallpaperStyle.Fill);
                SlideshowStatus = "已切换下一张";
                return;
            }

            // 未启用轮播：切换一张并同步启动轮播（开关置为开）
            var images = ScanImages(SlideshowFolder);
            if (images.Count == 0)
            {
                SlideshowStatus = "请先选择壁纸文件夹";
                return;
            }
            _wallpaper.StartSlideshow(images, TimeSpan.FromMinutes(SlideshowIntervalMinutes),
                SelectedStyle?.Value ?? WallpaperStyle.Fill, SlideshowShuffle, applyImmediately: true);
            SetSlideshowEnabledSilently(true);
            SlideshowStatus = "已切换一张（轮播已启动）";
        }
        catch (Exception ex)
        {
            SlideshowStatus = $"切换失败：{ex.Message}";
        }
    }

    // —— 外观命令 ——

    [RelayCommand]
    private void ToggleDarkMode()
    {
        try
        {
            _appearance.SetTheme(DarkMode);
        }
        catch (Exception ex)
        {
            StatusMessage($"深色模式设置失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleTransparency()
    {
        try
        {
            _appearance.SetTransparency(TransparencyEnabled);
        }
        catch (Exception ex)
        {
            StatusMessage($"透明度设置失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void ApplyAccentColor()
    {
        try
        {
            _appearance.SetAccentColor(ToDrawingColor(AccentColor));
            StatusMessage("强调色已应用");
        }
        catch (Exception ex)
        {
            StatusMessage($"强调色设置失败：{ex.Message}");
        }
    }

    // —— 辅助 ——

    /// <summary>填充方式下拉选项。</summary>
    public sealed record StyleOption(WallpaperStyle Value, string Display);

    private static List<string> ScanImages(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
    }

    private static System.Drawing.Color ToDrawingColor(Color c) =>
        System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

    private static Color ToMediaColor(System.Drawing.Color c) =>
        Color.FromArgb(c.A, c.R, c.G, c.B);

    private void StatusMessage(string message) => AppearanceStatus = message;

    public void Dispose()
    {
        _wallpaper.Dispose();
        GC.SuppressFinalize(this);
    }
}
