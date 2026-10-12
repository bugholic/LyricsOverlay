using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LyricsOverlay;

/// <summary>
/// Decides which lyrics to show for the current track:
///   1. lyrics the player page shows (via the browser extension), always first;
///   2. LRCLIB, only if the user allowed external lyrics.
/// Everything here runs on the UI thread.
/// </summary>
public sealed class LyricsController
{
    // How long to wait for the extension to load the player's lyrics before looking elsewhere.
    static readonly TimeSpan PlayerGrace = TimeSpan.FromSeconds(4);

    readonly AppSettings _settings;
    readonly MediaWatcher _media;
    readonly ExtensionBridge _bridge;
    readonly LrcLibClient _lrclib = new();
    readonly List<PlayerLyrics> _playerLyrics = new(); // newest last
    PlayerState? _playerState;

    CancellationTokenSource? _cts;
    LyricsDoc? _external;
    LyricsDoc? _picked;
    bool _waitingForPlayer;
    bool _searching;
    bool _searchFailed;

    public LyricsDoc? Lyrics { get; private set; }
    public string Status { get; private set; } = "";
    public TrackInfo? Track => _media.Snapshot.Track;
    public bool IsPlaying => _media.Snapshot.IsPlaying || (FreshPlayerState() is { Playing: true });

    public event Action? Changed;

    public LyricsController(AppSettings settings, MediaWatcher media, ExtensionBridge bridge)
    {
        _settings = settings;
        _media = media;
        _bridge = bridge;
        _media.TrackChanged += OnTrackChanged;
        _bridge.LyricsReceived += OnPlayerLyrics;
        _bridge.StateReceived += s => _playerState = s;
        _settings.Changed += OnSettingsChanged;
    }

    public TimeSpan CurrentPosition()
    {
        var now = DateTime.UtcNow;
        if (FreshPlayerState() is { } ps)
            return ps.Position + (ps.Playing ? now - ps.ReceivedAtUtc : TimeSpan.Zero);

        var snap = _media.Snapshot;
        return snap.Position + (snap.IsPlaying ? now - snap.TakenAtUtc : TimeSpan.Zero);
    }

    PlayerState? FreshPlayerState()
    {
        var ps = _playerState;
        var track = Track;
        if (ps is null || track is null) return null;
        if (DateTime.UtcNow - ps.ReceivedAtUtc > TimeSpan.FromSeconds(3)) return null;
        return TextUtil.SameTitle(ps.Title, track.Title) ? ps : null;
    }

    void OnTrackChanged(TrackInfo? track)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _external = null;
        _picked = null;
        _searchFailed = false;
        _searching = false;
        _waitingForPlayer = false;
        Lyrics = null;

        if (track is null)
        {
            Status = "";
            Changed?.Invoke();
            return;
        }
        _ = ResolveAsync(track, _cts.Token);
    }

    async Task ResolveAsync(TrackInfo track, CancellationToken ct)
    {
        try
        {
            if (FindPlayerLyrics(track) is null && _bridge.IsConnected)
            {
                _waitingForPlayer = true;
                Decide();
                await Task.Delay(PlayerGrace, ct);
                _waitingForPlayer = false;
            }

            var player = FindPlayerLyrics(track)?.Doc;
            var needExternal = _settings.AllowExternalLyrics &&
                               (player is null || (_settings.PreferSyncedExternal && !player.Synced));
            if (needExternal)
            {
                _searching = true;
                Decide();
                _external = await _lrclib.FindAsync(track, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // a newer track took over
        }
        catch (Exception)
        {
            _searchFailed = true;
        }

        if (ct.IsCancellationRequested) return;
        _searching = false;
        _waitingForPlayer = false;
        Decide();
    }

    void OnPlayerLyrics(PlayerLyrics lyrics)
    {
        _playerLyrics.RemoveAll(p => TextUtil.SameTitle(p.Title, lyrics.Title));
        _playerLyrics.Add(lyrics);
        if (_playerLyrics.Count > 50) _playerLyrics.RemoveAt(0);

        if (Track is { } track && TextUtil.SameTitle(lyrics.Title, track.Title))
        {
            // The player answered: if it has none and external is allowed, don't sit out the grace period.
            if (lyrics.Doc is null && _waitingForPlayer) OnTrackChanged(track);
            else Decide();
        }
    }

    void OnSettingsChanged()
    {
        var track = Track;
        if (track is null) return;

        var player = FindPlayerLyrics(track)?.Doc;
        var wantExternal = _settings.AllowExternalLyrics &&
                           (player is null || (_settings.PreferSyncedExternal && !player.Synced));
        if (wantExternal && _external is null && !_searching && !_waitingForPlayer)
            OnTrackChanged(track); // re-run the lookup now that it's allowed
        else
            Decide();
    }

    PlayerLyrics? FindPlayerLyrics(TrackInfo track) =>
        _playerLyrics.LastOrDefault(p => TextUtil.SameTitle(p.Title, track.Title));

    void Decide()
    {
        var track = Track;
        if (track is null) return;

        var player = FindPlayerLyrics(track)?.Doc;
        var external = _settings.AllowExternalLyrics ? _external : null;

        LyricsDoc? pick;
        if (player is not null && external is not null)
            pick = _settings.PreferSyncedExternal && external.Synced && !player.Synced ? external : player;
        else
            pick = player ?? external;

        if (pick is not null)
        {
            if (!ReferenceEquals(pick, _picked))
            {
                _picked = pick;
                Lyrics = pick.Synced
                    ? pick
                    : new LyricsDoc
                    {
                        Lines = TextUtil.EstimateTiming(pick.Lines.Select(l => l.Text), track.Duration),
                        Synced = false,
                        TimingEstimated = true,
                        Source = pick.Source,
                        IsExternal = pick.IsExternal,
                    };
            }
            Status = "";
            Changed?.Invoke();
            return;
        }

        _picked = null;
        Lyrics = null;
        Status = (_waitingForPlayer || _searching) ? "Looking for lyrics…"
            : _searchFailed ? "Couldn't reach LRCLIB."
            : _settings.AllowExternalLyrics ? "No lyrics found for this song."
            : _bridge.IsConnected ? "This song has no lyrics in the player. External lyrics are off."
            : "No lyrics from the player (extension not connected). External lyrics are off.";
        Changed?.Invoke();
    }
}
