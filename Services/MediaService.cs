using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _lastTrackKey = "";

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public bool HasSession => _session != null;
    public bool IsPlaying { get; private set; }
    public TimeSpan Duration { get; private set; }
    public TimeSpan Position { get; private set; }
    public DateTimeOffset PositionTimestamp { get; private set; } = DateTimeOffset.Now;
    public BitmapImage? Thumbnail { get; private set; }

    /// <summary>Se dispara en el hilo de UI cuando cambia cualquier dato.</summary>
    public event Action? Changed;

    private System.Windows.Threading.Dispatcher Ui => System.Windows.Application.Current.Dispatcher;

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => Ui.BeginInvoke(SelectSession);
        _manager.SessionsChanged += (_, _) => Ui.BeginInvoke(SelectSession);
        SelectSession();
    }

    private void SelectSession()
    {
        if (_manager == null) return;
        var chosen = _manager.GetSessions().FirstOrDefault(s =>
                         s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                     ?? _manager.GetCurrentSession();

        if (!ReferenceEquals(chosen, _session))
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMedia;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            _session = chosen;
            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnMedia;
                _session.PlaybackInfoChanged += OnPlayback;
                _session.TimelinePropertiesChanged += OnTimeline;
            }
        }
        _ = RefreshAllAsync();
    }

    private void OnMedia(GlobalSystemMediaTransportControlsSession s, object a) => Ui.BeginInvoke(() => _ = RefreshMediaAsync());
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, object a) => Ui.BeginInvoke(() => { RefreshPlayback(); RefreshTimeline(); Changed?.Invoke(); });
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, object a) => Ui.BeginInvoke(() => { RefreshTimeline(); Changed?.Invoke(); });

    private async Task RefreshAllAsync()
    {
        RefreshPlayback();
        RefreshTimeline();
        await RefreshMediaAsync();
    }

    private void RefreshPlayback()
    {
        var info = _session?.GetPlaybackInfo();
        IsPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    }

    private void RefreshTimeline()
    {
        var t = _session?.GetTimelineProperties();
        if (t == null) { Duration = Position = TimeSpan.Zero; return; }
        Duration = t.EndTime - t.StartTime;
        Position = t.Position - t.StartTime;
        PositionTimestamp = t.LastUpdatedTime;
    }

    private async Task RefreshMediaAsync()
    {
        if (_session == null)
        {
            Title = Artist = "";
            Thumbnail = null;
            _lastTrackKey = "";
            Changed?.Invoke();
            return;
        }
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            Title = props.Title ?? "";
            Artist = props.Artist ?? "";
            var key = Title + "|" + Artist + "|" + props.AlbumTitle;
            if (key != _lastTrackKey)
            {
                _lastTrackKey = key;
                Thumbnail = await LoadThumbnailAsync(props.Thumbnail);
            }
        }
        catch { /* sesión cerrada durante la consulta */ }
        Changed?.Invoke();
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference == null) return null;
        try
        {
            using var ras = await reference.OpenReadAsync();
            if (ras.Size == 0) return null;
            using var stream = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public TimeSpan CurrentPosition()
    {
        var pos = Position;
        if (IsPlaying) pos += DateTimeOffset.Now - PositionTimestamp;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
        return pos;
    }

    public async Task TogglePlayPauseAsync() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async Task NextAsync() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
    public async Task PreviousAsync() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }
}
