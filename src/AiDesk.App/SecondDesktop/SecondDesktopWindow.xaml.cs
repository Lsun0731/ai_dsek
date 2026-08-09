using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiDesk.App.Services;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.SecondDesktop;

namespace AiDesk.App.SecondDesktop;

/// <summary>磁贴数据（运行中应用）。</summary>
public sealed record TileModel
{
    public required string Title { get; init; }
    public required int ProcessId { get; init; }
    public ImageSource? Icon { get; init; }
}

/// <summary>
/// 第二桌面全屏覆盖层：搜索启动应用 + 运行中应用磁贴 + 音乐监控。
/// 进入/退出时的任务栏/图标隐藏恢复由 SecondDesktopController 负责。
/// </summary>
public partial class SecondDesktopWindow : Window
{
    private readonly RunningAppsProvider _runningApps = new();
    private readonly MediaSessionService _media = new();
    private readonly DispatcherTimer _appsTimer;
    private readonly IReadOnlyList<StartMenuApp> _startMenuApps;

    /// <summary>磁贴点击：请求启动/激活指定进程（由控制器隐藏启动器并激活，保持第二桌面模式）。</summary>
    public event Action<int>? LaunchRequested;

    /// <summary>Esc/返回按钮：请求退出第二桌面模式（由控制器恢复任务栏/图标并关闭窗口）。</summary>
    public event Action? ExitRequested;

    public SecondDesktopWindow()
    {
        InitializeComponent();

        _startMenuApps = StartMenuAppsProvider.Scan();
        RefreshRunningApps();

        _appsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _appsTimer.Tick += (_, _) => RefreshRunningApps();
        _appsTimer.Start();
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        await _media.StartAsync();
        // await 期间用户可能已关闭窗口（如按 Esc）
        if (!IsLoaded)
            return;
        _media.TrackChanged += OnTrackChanged;
        _media.PlaybackChanged += OnPlaybackChanged;
        OnTrackChanged(_media.CurrentTrack);
        OnPlaybackChanged(_media.IsPlaying);
    }

    // ---- 运行中应用磁贴 ----

    private void RefreshRunningApps()
    {
        try
        {
            var apps = _runningApps.Refresh();
            TilesHost.ItemsSource = apps.Select(a => new TileModel
            {
                Title = a.Title,
                ProcessId = a.ProcessId,
                Icon = IconHelper.GetExecutableIcon(a.ExecutablePath),
            }).ToList();
            CountText.Text = $"{apps.Count} 个应用";
        }
        catch (Exception ex)
        {
            Telemetry.Error("SecondDesktop.RefreshApps", ex);
        }
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int processId })
            LaunchRequested?.Invoke(processId);
    }

    // ---- 搜索 ----

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        List<StartMenuApp> results;
        if (string.IsNullOrEmpty(query))
            results = [];
        else
            results = _startMenuApps
                .Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Take(12)
                .ToList();
        SearchList.ItemsSource = results;
        SearchList.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchList.Items.Count > 0 && SearchList.ItemsSource is not null)
            LaunchApp((StartMenuApp)SearchList.Items[0]);
    }

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is StartMenuApp app)
            LaunchApp(app);
    }

    private void LaunchApp(StartMenuApp app)
    {
        try
        {
            Process.Start(new ProcessStartInfo(app.LnkPath) { UseShellExecute = true });
            Telemetry.Function("SecondDesktop.Launch", true, 0, $"app={app.Name}");
            // 启动新应用后隐藏启动器，留在第二桌面模式（任务栏仍隐藏）
            Hide();
        }
        catch (Exception ex)
        {
            Telemetry.Function("SecondDesktop.Launch", false, 0, $"app={app.Name} err={ex.Message}");
        }
    }

    // ---- 音乐监控 ----

    private void OnTrackChanged(MediaTrackInfo? track)
    {
        Dispatcher.Invoke(() =>
        {
            if (track is null)
            {
                MusicTitle.Text = "未在播放";
                MusicArtist.Text = "正在监听系统媒体…";
                MusicCover.Source = null;
                return;
            }
            MusicTitle.Text = string.IsNullOrWhiteSpace(track.Title) ? "未知曲目" : track.Title;
            MusicArtist.Text = string.IsNullOrWhiteSpace(track.Artist)
                ? track.Album
                : $"{track.Artist}{(string.IsNullOrWhiteSpace(track.Album) ? "" : " — " + track.Album)}";
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

    // ---- 退出 ----

    private void OnBackClicked(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            ExitRequested?.Invoke();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        _appsTimer.Stop();
        _media.TrackChanged -= OnTrackChanged;
        _media.PlaybackChanged -= OnPlaybackChanged;
        _media.Dispose();
    }
}
