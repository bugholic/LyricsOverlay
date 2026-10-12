using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media.Control;

namespace LyricsOverlay;

/// <summary>
/// Follows what's playing through Windows' media sessions (the same feed as the volume flyout's
/// media controls). Chrome, Edge, Firefox, Brave, Opera and Vivaldi all publish to it.
/// </summary>
public sealed class MediaWatcher
{
    static readonly string[] BrowserIds =
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "thebrowser", "arc",
        "308046b0af4a39cb", // Firefox's app id
    };

    readonly AppSettings _settings;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    TrackInfo? _track;
    bool _polling;

    public PlaybackSnapshot Snapshot { get; private set; } = new(null, TimeSpan.Zero, DateTime.UtcNow, false);

    public event Action<TrackInfo?>? TrackChanged;

    public MediaWatcher(AppSettings settings)
    {
        _settings = settings;
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        await PollAsync();
        _timer.Start();
    }

    public static bool IsBrowser(string appId)
    {
        var id = appId.ToLowerInvariant();
        return BrowserIds.Any(id.Contains);
    }

    async Task PollAsync()
    {
        if (_manager is null || _polling) return;
        _polling = true;
        try
        {
            var session = PickSession();
            TrackInfo? track = null;
            var position = TimeSpan.Zero;
            var playing = false;

            if (session is not null)
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();
                playing = session.GetPlaybackInfo().PlaybackStatus ==
                          GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                if (props is not null && !string.IsNullOrWhiteSpace(props.Title))
                {
                    var duration = timeline.EndTime - timeline.StartTime;
                    track = new TrackInfo(props.Title, props.Artist ?? "", props.AlbumTitle ?? "",
                        duration > TimeSpan.Zero ? duration : TimeSpan.Zero, session.SourceAppUserModelId);
                }
                else
                {
                    // Players briefly clear metadata between songs; don't treat that as a new track.
                    track = _track;
                }

                position = timeline.Position;
                if (playing && timeline.LastUpdatedTime.Year > 2000)
                {
                    var sinceUpdate = DateTimeOffset.Now - timeline.LastUpdatedTime;
                    if (sinceUpdate > TimeSpan.Zero && sinceUpdate < TimeSpan.FromHours(1))
                        position += sinceUpdate;
                }
            }

            Snapshot = new PlaybackSnapshot(track, position, DateTime.UtcNow, playing);

            var changed = track?.Key != _track?.Key;
            _track = track;
            if (changed) TrackChanged?.Invoke(track);
        }
        catch (Exception)
        {
            // The session can disappear mid-poll (tab closed). Next tick picks up whatever is left.
        }
        finally
        {
            _polling = false;
        }
    }

    GlobalSystemMediaTransportControlsSession? PickSession()
    {
        var sessions = _manager!.GetSessions()
            .Where(s => !_settings.BrowsersOnly || IsBrowser(s.SourceAppUserModelId))
            .ToList();
        if (sessions.Count == 0) return null;

        var playing = sessions.FirstOrDefault(s =>
            s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        if (playing is not null) return playing;

        var current = _manager.GetCurrentSession();
        return sessions.FirstOrDefault(s => s.SourceAppUserModelId == current?.SourceAppUserModelId) ?? sessions[0];
    }
}
