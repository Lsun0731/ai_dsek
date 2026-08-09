using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.SecondDesktop;

namespace AiDesk.App.DesktopDock;

/// <summary>磁贴数据（运行中应用）。</summary>
public sealed record DockTileModel
{
    public required string Title { get; init; }
    public required int ProcessId { get; init; }
    public ImageSource? Icon { get; init; }
}

/// <summary>
/// 桌面 Dock：挂载到桌面图层（壁纸之上、应用之下），显示运行中应用磁贴 + 音乐卡片。
/// 点击磁贴激活应用，应用窗口浮于 Dock 之上（Dock 属于桌面层）。
/// </summary>
public partial class DesktopDockWindow : Window
{
    private readonly RunningAppsProvider _runningApps = new();
    private readonly MediaSessionService _media = new();
    private readonly DispatcherTimer _appsTimer;
    private SearchOverlayWindow? _searchOverlay;

    /// <summary>挂载失败降级标志（挂到普通置顶窗口）。</summary>
    public bool AttachedToDesktop { get; private set; }

    public DesktopDockWindow()
    {
        InitializeComponent();

        // 位置：底部居中（内容自适应宽度，SizeChanged 时重新居中）
        Top = SystemParameters.PrimaryScreenHeight - 104;
        SizeChanged += (_, _) =>
        {
            if (ActualWidth > 0)
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        };

        RefreshRunningApps();

        _appsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _appsTimer.Tick += (_, _) => RefreshRunningApps();
        _appsTimer.Start();

        Closing += OnWindowClosing;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnWindowLoaded;
    }

    /// <summary>窗口句柄就绪后挂载到桌面图层；失败则降级为普通置顶窗口。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            AttachedToDesktop = DesktopLayerHost.AttachToDesktop(hwnd);
            if (!AttachedToDesktop)
            {
                Topmost = true; // 降级：无法挂桌面层时置顶显示
                Telemetry.Info("Dock", "桌面图层挂载失败，降级为置顶窗口");
            }
        }
        catch (Exception ex)
        {
            AttachedToDesktop = false;
            Topmost = true;
            Telemetry.Error("Dock.Attach", ex);
        }
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await _media.StartAsync();
        if (!IsLoaded)
            return;
        _media.TrackChanged += OnTrackChanged;
        _media.PlaybackChanged += OnPlaybackChanged;
        OnTrackChanged(_media.CurrentTrack);
        OnPlaybackChanged(_media.IsPlaying);
    }

    // ---- 磁贴 ----

    private void RefreshRunningApps()
    {
        try
        {
            var apps = _runningApps.Refresh();
            TilesHost.ItemsSource = apps.Select(a => new DockTileModel
            {
                Title = a.Title,
                ProcessId = a.ProcessId,
                Icon = IconHelper.GetExecutableIcon(a.ExecutablePath),
            }).ToList();
        }
        catch (Exception ex)
        {
            Telemetry.Error("Dock.RefreshApps", ex);
        }
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int processId })
            ActivateProcess(processId);
    }

    private static void ActivateProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.MainWindowHandle != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(process.MainWindowHandle);
        }
        catch
        {
            // 进程已退出则忽略
        }
    }

    // ---- 搜索 ----

    private void OnSearchClicked(object sender, RoutedEventArgs e) => ShowSearch();

    /// <summary>呼出搜索浮层（热键也调用）。</summary>
    public void ShowSearch()
    {
        if (_searchOverlay is null)
        {
            _searchOverlay = new SearchOverlayWindow();
            _searchOverlay.Closed += (_, _) => _searchOverlay = null;
        }
        _searchOverlay.Show();
        _searchOverlay.Activate();
    }

    // ---- 音乐 ----

    private void OnTrackChanged(MediaTrackInfo? track)
    {
        Dispatcher.Invoke(() =>
        {
            if (track is null)
            {
                MusicTitle.Text = "未在播放";
                MusicArtist.Text = "";
                MusicCover.Source = null;
                return;
            }
            MusicTitle.Text = string.IsNullOrWhiteSpace(track.Title) ? "未知曲目" : track.Title;
            MusicArtist.Text = string.IsNullOrWhiteSpace(track.Artist) ? track.Album : track.Artist;
            MusicCover.Source = TrackToImage(track);
        });
    }

    private void OnPlaybackChanged(bool isPlaying)
    {
        Dispatcher.Invoke(() => PlayBtn.Content = isPlaying ? "⏸" : "▶");
    }

    private static ImageSource? TrackToImage(MediaTrackInfo track)
    {
        if (track.ThumbnailPng is not { Length: > 0 } bytes)
            return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private async void OnPlayClicked(object sender, RoutedEventArgs e) => await _media.TogglePlayAsync();
    private async void OnNextClicked(object sender, RoutedEventArgs e) => await _media.NextAsync();
    private async void OnPrevClicked(object sender, RoutedEventArgs e) => await _media.PrevAsync();

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        _appsTimer.Stop();
        _media.TrackChanged -= OnTrackChanged;
        _media.PlaybackChanged -= OnPlaybackChanged;
        _media.Dispose();
        // 连带关闭 Dock 弹出的搜索浮层，避免禁用 Dock 后浮层残留
        _searchOverlay?.Close();
    }
}
