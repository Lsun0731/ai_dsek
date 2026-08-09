using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiDesk.Core.Diagnostics;
using AiDesk.Core.SecondDesktop;

namespace AiDesk.App.Widgets;

/// <summary>音乐小组件：当前媒体会话的封面/标题/歌手 + 播放控制。</summary>
public partial class MusicWidgetWindow : WidgetWindowBase
{
    private readonly MediaSessionService _media = new();

    public MusicWidgetWindow() : base(Services.WidgetKind.Music, topmost: true)
    {
        InitializeComponent();
    }

    protected override void OnWidgetLoaded()
    {
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        await _media.StartAsync();
        if (!IsLoaded)
            return;
        _media.TrackChanged += OnTrackChanged;
        _media.PlaybackChanged += OnPlaybackChanged;
        OnTrackChanged(_media.CurrentTrack);
        OnPlaybackChanged(_media.IsPlaying);
    }

    protected override void OnTick()
    {
        // 音乐信息由系统媒体会话事件驱动，无需轮询
    }

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

    protected override void OnClosed(EventArgs e)
    {
        _media.TrackChanged -= OnTrackChanged;
        _media.PlaybackChanged -= OnPlaybackChanged;
        _media.Dispose();
        base.OnClosed(e);
    }
}
