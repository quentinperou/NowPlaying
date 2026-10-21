using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NowPlaying.Core;
using Windows.Media.Control;
using MediaManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace NowPlaying.Services;

/// <summary>What the widget and the overlays show: the track of the player chosen by <see cref="SourcePicker"/>.</summary>
internal sealed record MediaSnapshot(
    string Id,
    string AppId,
    string AppName,
    string Title,
    string Artist,
    string Album,
    BitmapSource? Cover,
    byte[]? CoverBytes,
    string CoverType,
    int CoverRevision,
    Rgb? CoverColor,
    bool IsPlaying,
    TimeSpan Position,
    DateTimeOffset PositionReportedAt,
    TimeSpan Duration,
    double Rate,
    bool CanPlayPause,
    bool CanNext,
    bool CanPrevious,
    bool CanSeek)
{
    public TimeSpan PositionNow => Timeline.Extrapolate(Position, PositionReportedAt, DateTimeOffset.Now, IsPlaying, Rate, Duration);
}

/// <summary>A player known to Windows, for the "Source" menu of the widget and the Sources page.</summary>
internal sealed record MediaSource(string Id, string AppId, string AppName, string Title, bool IsPlaying, bool IsCurrent);

/// <summary>
/// The media sessions of Windows (the players listed in the volume flyout: Spotify, the browsers, Media Player…):
/// follows them all, picks the one to show, reads its track, cover and position, and sends it the commands of the
/// widget. Everything happens on the UI thread; Windows' events are only marshalled to it. Nothing polls.
/// </summary>
internal sealed class MediaSessions
{
    private readonly Dispatcher _ui;
    private readonly SourcePicker _picker = new();
    private readonly Dictionary<string, Tracked> _tracked = [];
    private MediaManager? _manager;
    private int _coverRevisions;

    private sealed class Tracked(string id, MediaSession session)
    {
        public string Id { get; } = id;
        public MediaSession Session { get; } = session;
        public string Title = "", Artist = "", Album = "";
        public BitmapSource? Cover;
        public byte[]? CoverBytes;
        public string CoverType = "image/png";
        public Rgb? CoverColor;
        public int CoverRevision;
        public int PropertiesVersion;
        public Windows.Foundation.TypedEventHandler<MediaSession, MediaPropertiesChangedEventArgs>? OnProperties;
        public Windows.Foundation.TypedEventHandler<MediaSession, PlaybackInfoChangedEventArgs>? OnPlayback;
        public Windows.Foundation.TypedEventHandler<MediaSession, TimelinePropertiesChangedEventArgs>? OnTimeline;
    }

    public MediaSessions(Dispatcher ui) => _ui = ui;

    /// <summary>The track shown, null when no player is known to Windows.</summary>
    public MediaSnapshot? Current { get; private set; }

    /// <summary>The track, its cover, the playback state or the position changed (on the UI thread).</summary>
    public event EventHandler? Changed;

    /// <summary>The players offered in the menu of the widgets (not those hidden on the Sources page).</summary>
    public IReadOnlyList<MediaSource> Sources => [.. Players.Where(s => !_picker.IsHidden(s.Id))];

    /// <summary>All the players known to Windows now.</summary>
    public IReadOnlyList<MediaSource> Players =>
        [.. _tracked.Values.Select(t =>
        {
            string appId = t.Session.SourceAppUserModelId ?? "";
            return new MediaSource(t.Id, appId, AppNames.Of(appId), t.Title, _picker.IsPlaying(t.Id), t.Id == _picker.Current);
        })];

    /// <summary>The rules of the Sources page changed.</summary>
    public void SetPolicy(SourcePolicy policy)
    {
        if (_picker.SetPolicy(policy))
            Publish();
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await MediaManager.RequestAsync();
        }
        catch (Exception ex)
        {
            // Windows without media features (N editions): the widget just never shows.
            Debug.WriteLine($"Media sessions unavailable: {ex.Message}");
            return;
        }
        _manager.SessionsChanged += (_, _) => _ui.BeginInvoke(SyncSessions);
        SyncSessions();
    }

    // ---------------------------------------------------------------- Commands

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync().AsTask());

    public Task NextAsync() => Run(s => s.TrySkipNextAsync().AsTask());

    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync().AsTask());

    /// <summary>Moves to <paramref name="fraction"/> (0 to 1) of the track.</summary>
    public Task SeekAsync(double fraction)
    {
        if (Current is not { CanSeek: true, Duration.Ticks: > 0 } current)
            return Task.CompletedTask;
        return Run(s =>
        {
            var timeline = s.GetTimelineProperties();
            long ticks = timeline.StartTime.Ticks + (long)(current.Duration.Ticks * Math.Clamp(fraction, 0, 1));
            return s.TryChangePlaybackPositionAsync(ticks).AsTask();
        });
    }

    /// <summary>The user chose this player in the menu of the widget.</summary>
    public void Pin(string id)
    {
        if (_picker.Pin(id))
            Publish();
    }

    private async Task Run(Func<MediaSession, Task<bool>> command)
    {
        if (Current is null || !_tracked.TryGetValue(Current.Id, out var tracked))
            return;
        try
        {
            await command(tracked.Session);
        }
        catch (Exception ex)
        {
            // The player went away meanwhile.
            Debug.WriteLine($"Media command failed: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- Sessions

    private void SyncSessions()
    {
        if (_manager is null)
            return;
        IReadOnlyList<MediaSession> sessions;
        try
        {
            sessions = _manager.GetSessions();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetSessions failed: {ex.Message}");
            return;
        }

        // Two sessions of the same app (two browsers windows) get "#2", "#3"… in the order Windows gives them.
        var now = new Dictionary<string, MediaSession>();
        foreach (var session in sessions)
        {
            string baseId = session.SourceAppUserModelId ?? "";
            string id = baseId;
            for (int n = 2; now.ContainsKey(id); n++)
                id = $"{baseId}#{n}";
            now[id] = session;
        }

        bool changed = false;
        foreach (string gone in _tracked.Keys.Where(id => !now.ContainsKey(id)).ToList())
        {
            Unhook(_tracked[gone]);
            _tracked.Remove(gone);
            changed |= _picker.Remove(gone);
        }
        foreach (var (id, session) in now)
        {
            if (_tracked.ContainsKey(id))
                continue;
            var tracked = new Tracked(id, session);
            Hook(tracked);
            _tracked[id] = tracked;
            changed |= _picker.Update(id, IsPlaying(session));
            _ = RefreshPropertiesAsync(tracked);
        }
        if (changed || Current is null != (_picker.Current is null))
            Publish();
    }

    private void Hook(Tracked t)
    {
        t.OnProperties = (_, _) => _ui.BeginInvoke(() => _ = RefreshPropertiesAsync(t));
        t.OnPlayback = (_, _) => _ui.BeginInvoke(() => PlaybackChanged(t));
        t.OnTimeline = (_, _) => _ui.BeginInvoke(() =>
        {
            if (t.Id == _picker.Current)
                Publish();
        });
        t.Session.MediaPropertiesChanged += t.OnProperties;
        t.Session.PlaybackInfoChanged += t.OnPlayback;
        t.Session.TimelinePropertiesChanged += t.OnTimeline;
    }

    private static void Unhook(Tracked t)
    {
        try
        {
            t.Session.MediaPropertiesChanged -= t.OnProperties;
            t.Session.PlaybackInfoChanged -= t.OnPlayback;
            t.Session.TimelinePropertiesChanged -= t.OnTimeline;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unhook failed: {ex.Message}");
        }
    }

    private void PlaybackChanged(Tracked t)
    {
        if (!_tracked.ContainsKey(t.Id))
            return;
        bool changed = _picker.Update(t.Id, IsPlaying(t.Session));
        if (changed || t.Id == _picker.Current)
            Publish();
    }

    private static bool IsPlaying(MediaSession session)
    {
        try
        {
            return session.GetPlaybackInfo()?.PlaybackStatus == PlaybackStatus.Playing;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task RefreshPropertiesAsync(Tracked t)
    {
        int version = ++t.PropertiesVersion;
        GlobalSystemMediaTransportControlsSessionMediaProperties? properties;
        try
        {
            properties = await t.Session.TryGetMediaPropertiesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Media properties failed: {ex.Message}");
            return;
        }
        if (properties is null || version != t.PropertiesVersion || !_tracked.ContainsKey(t.Id))
            return;

        t.Title = properties.Title ?? "";
        t.Artist = !string.IsNullOrWhiteSpace(properties.Artist) ? properties.Artist : properties.AlbumArtist ?? "";
        t.Album = properties.AlbumTitle ?? "";

        byte[]? bytes = null;
        string type = "image/png";
        if (properties.Thumbnail is { } thumbnail)
        {
            try
            {
                using var stream = await thumbnail.OpenReadAsync();
                type = string.IsNullOrEmpty(stream.ContentType) ? type : stream.ContentType;
                using var input = stream.AsStreamForRead();
                using var memory = new MemoryStream();
                await input.CopyToAsync(memory);
                bytes = memory.ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cover failed: {ex.Message}");
            }
            if (version != t.PropertiesVersion)
                return;
        }
        if (bytes is null || t.CoverBytes is null || !bytes.AsSpan().SequenceEqual(t.CoverBytes))
        {
            t.CoverBytes = bytes;
            t.CoverType = type;
            (t.Cover, t.CoverColor) = Decode(bytes);
            // Counted over all the players: the overlay asks for /cover?r=…, the same address for another player's cover
            // would keep the old picture.
            t.CoverRevision = ++_coverRevisions;
        }
        if (t.Id == _picker.Current)
            Publish();
    }

    // The cover at 256 px at most (the widget shows 32, the card 114 at 200 %), and its color.
    internal static (BitmapSource?, Rgb?) Decode(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return (null, null);
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 256;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();

            var small = new TransformedBitmap(image, new ScaleTransform(64.0 / image.PixelWidth, 64.0 / image.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);
            return (image, DominantColor.FromBgra(pixels, bgra.PixelWidth, bgra.PixelHeight, stride));
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"Cover decoding failed: {ex.Message}");
            return (null, null);
        }
    }

    private void Publish()
    {
        Current = _picker.Current is { } id && _tracked.TryGetValue(id, out var t) ? Snapshot(t) : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static MediaSnapshot Snapshot(Tracked t)
    {
        bool playing = false, canPlayPause = false, canNext = false, canPrevious = false, canSeek = false;
        double rate = 1;
        TimeSpan position = TimeSpan.Zero, duration = TimeSpan.Zero;
        DateTimeOffset reportedAt = DateTimeOffset.Now;
        try
        {
            var info = t.Session.GetPlaybackInfo();
            playing = info.PlaybackStatus == PlaybackStatus.Playing;
            canPlayPause = info.Controls.IsPlayPauseToggleEnabled || info.Controls.IsPlayEnabled || info.Controls.IsPauseEnabled;
            canNext = info.Controls.IsNextEnabled;
            canPrevious = info.Controls.IsPreviousEnabled;
            canSeek = info.Controls.IsPlaybackPositionEnabled;
            rate = info.PlaybackRate ?? 1;
            var timeline = t.Session.GetTimelineProperties();
            duration = timeline.EndTime - timeline.StartTime;
            position = timeline.Position - timeline.StartTime;
            reportedAt = timeline.LastUpdatedTime;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Playback info failed: {ex.Message}");
        }
        string appId = t.Session.SourceAppUserModelId ?? "";
        return new MediaSnapshot(t.Id, appId, AppNames.Of(appId), t.Title, t.Artist, t.Album,
            t.Cover, t.CoverBytes, t.CoverType, t.CoverRevision, t.CoverColor,
            playing, position, reportedAt, duration < TimeSpan.Zero ? TimeSpan.Zero : duration, rate,
            canPlayPause, canNext, canPrevious, canSeek && duration > TimeSpan.Zero);
    }
}
