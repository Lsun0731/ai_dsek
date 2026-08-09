using Windows.Media.Control;
using Windows.Storage.Streams;

namespace AiDesk.Core.SecondDesktop;

/// <summary>正在播放的曲目元数据（系统媒体会话）。</summary>
public sealed record MediaTrackInfo
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string? SourceAppId { get; init; }
    public byte[]? ThumbnailPng { get; init; }
}

/// <summary>
/// 通过系统媒体会话（GlobalSystemMediaTransportControlsSessionManager）读取当前播放的媒体元数据，
/// 支持标题/歌手/专辑/封面 + 播放控制（播放/暂停/上一首/下一首）。无需 API key。
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    public MediaTrackInfo? CurrentTrack { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsAvailable { get; private set; }

    /// <summary>曲目变化时触发（null = 无媒体会话）。</summary>
    public event Action<MediaTrackInfo?>? TrackChanged;

    /// <summary>播放/暂停状态变化。</summary>
    public event Action<bool>? PlaybackChanged;

    /// <summary>媒体会话 API 可用性变化。</summary>
    public event Action<bool>? AvailabilityChanged;

    /// <summary>初始化并绑定当前媒体会话。必须在 STA 线程（WPF UI 线程）调用。</summary>
    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnSessionChanged;
            BindSession(_manager.GetCurrentSession());
            IsAvailable = true;
            AvailabilityChanged?.Invoke(true);
            await RefreshAsync();
        }
        catch
        {
            IsAvailable = false;
            AvailabilityChanged?.Invoke(false);
        }
    }

    /// <summary>手动刷新当前曲目与播放状态。</summary>
    public async Task RefreshAsync()
    {
        await RefreshTrackAsync();
        RefreshPlayback();
    }

    private void BindSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
        _session = session;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        => BindSession(sender.GetCurrentSession());

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => await RefreshTrackAsync();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => RefreshPlayback();

    private async Task RefreshTrackAsync()
    {
        if (_session is null)
        {
            SetTrack(null);
            return;
        }
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            var track = new MediaTrackInfo
            {
                Title = props.Title ?? "",
                Artist = props.Artist ?? "",
                Album = props.AlbumTitle ?? "",
                SourceAppId = _session.SourceAppUserModelId,
                ThumbnailPng = await ReadThumbnailAsync(props.Thumbnail),
            };
            SetTrack(track);
        }
        catch
        {
            SetTrack(null);
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
            return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)bytes.Length);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private void SetTrack(MediaTrackInfo? track)
    {
        var changed = (CurrentTrack is null) != (track is null) ||
                      (CurrentTrack is not null && track is not null &&
                       (CurrentTrack.Title != track.Title || CurrentTrack.Artist != track.Artist));
        if (!changed)
            return;
        CurrentTrack = track;
        TrackChanged?.Invoke(track);
    }

    private void RefreshPlayback()
    {
        var playing = _session?.GetPlaybackInfo()?.PlaybackStatus ==
                      GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (IsPlaying == playing)
            return;
        IsPlaying = playing;
        PlaybackChanged?.Invoke(playing);
    }

    // ---- 播放控制 ----

    public async Task TogglePlayAsync()
    {
        if (_session is null)
            return;
        try
        {
            if (IsPlaying)
                await _session.TryPauseAsync();
            else
                await _session.TryPlayAsync();
        }
        catch
        {
            // 播放器可能不支持，忽略
        }
    }

    public async Task NextAsync()
    {
        if (_session is null)
            return;
        try { await _session.TrySkipNextAsync(); } catch { }
    }

    public async Task PrevAsync()
    {
        if (_session is null)
            return;
        try { await _session.TrySkipPreviousAsync(); } catch { }
    }

    public void Dispose()
    {
        if (_manager is not null)
            _manager.CurrentSessionChanged -= OnSessionChanged;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
    }
}
