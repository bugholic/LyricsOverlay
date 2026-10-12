using System;
using System.Collections.Generic;

namespace LyricsOverlay;

public sealed record TrackInfo(string Title, string Artist, string Album, TimeSpan Duration, string SourceApp)
{
    public string Key => TextUtil.Normalize(Title) + "|" + TextUtil.Normalize(Artist);
}

public sealed record LyricLine(TimeSpan Time, string Text);

public sealed class LyricsDoc
{
    public required IReadOnlyList<LyricLine> Lines { get; init; }

    /// <summary>True when every line has a real timestamp (LRC); false for plain text.</summary>
    public required bool Synced { get; init; }

    /// <summary>True when plain lyrics were spread over the song length to approximate timing.</summary>
    public bool TimingEstimated { get; init; }

    /// <summary>Human-readable origin, e.g. "YouTube Music" or "LRCLIB".</summary>
    public required string Source { get; init; }

    public bool IsExternal { get; init; }
}

public sealed record PlaybackSnapshot(TrackInfo? Track, TimeSpan Position, DateTime TakenAtUtc, bool IsPlaying);

/// <summary>Lyrics reported by the browser extension for whatever the player page is showing.</summary>
public sealed record PlayerLyrics(string Title, string Artist, LyricsDoc? Doc, DateTime ReceivedAtUtc);

/// <summary>Playback position reported by the browser extension (more precise than the media session's).</summary>
public sealed record PlayerState(string Title, string Artist, string Source, TimeSpan Position, bool Playing, DateTime ReceivedAtUtc);
