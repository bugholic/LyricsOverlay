using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LyricsOverlay;

/// <summary>
/// LRCLIB (https://lrclib.net) — free, keyless lyrics database with time-synced LRC lyrics.
/// Only called when the user has allowed external lyrics.
/// </summary>
public sealed class LrcLibClient
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LyricsOverlay/0.1 (Windows desktop lyrics overlay)");
        return http;
    }

    public async Task<LyricsDoc?> FindAsync(TrackInfo track, CancellationToken ct)
    {
        var (title, artist) = TextUtil.CleanForSearch(track.Title, track.Artist);
        var seconds = (int)Math.Round(track.Duration.TotalSeconds);

        // Exact lookup needs all four fields; LRCLIB matches duration within a couple of seconds.
        Record? hit = null;
        if (!string.IsNullOrWhiteSpace(track.Album) && seconds > 0)
        {
            hit = await GetAsync<Record>(
                $"api/get?track_name={E(title)}&artist_name={E(artist)}&album_name={E(track.Album)}&duration={seconds}", ct);
        }

        if (hit is null || !hit.HasLyrics)
        {
            var results = await GetAsync<List<Record>>($"api/search?track_name={E(title)}&artist_name={E(artist)}", ct);
            if (results is null || results.Count == 0)
                results = await GetAsync<List<Record>>($"api/search?q={E($"{artist} {title}")}", ct);
            hit = Best(results ?? new List<Record>(), title, track.Duration);
        }

        return hit?.ToDoc();
    }

    static Record? Best(List<Record> results, string title, TimeSpan duration) =>
        results
            .Where(r => r.HasLyrics && TextUtil.SameTitle(r.TrackName ?? "", title))
            .Select(r => (r, score: Score(r, duration)))
            .Where(x => x.score > double.MinValue)
            .OrderByDescending(x => x.score)
            .Select(x => x.r)
            .FirstOrDefault();

    static double Score(Record r, TimeSpan duration)
    {
        double score = string.IsNullOrWhiteSpace(r.SyncedLyrics) ? 0 : 10;
        if (duration > TimeSpan.Zero && r.Duration > 0)
        {
            var off = Math.Abs(r.Duration - duration.TotalSeconds);
            if (off > 15) return double.MinValue; // probably a different version/edit
            score -= off;
        }
        return score;
    }

    static async Task<T?> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var res = await Http.GetAsync(url, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    static string E(string s) => Uri.EscapeDataString(s);

    sealed class Record
    {
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public double Duration { get; set; }
        public bool Instrumental { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }

        public bool HasLyrics => Instrumental || !string.IsNullOrWhiteSpace(SyncedLyrics) || !string.IsNullOrWhiteSpace(PlainLyrics);

        public LyricsDoc? ToDoc()
        {
            if (Instrumental)
                return new LyricsDoc { Lines = new[] { new LyricLine(TimeSpan.Zero, "♪ Instrumental ♪") }, Synced = true, Source = "LRCLIB", IsExternal = true };

            if (!string.IsNullOrWhiteSpace(SyncedLyrics))
            {
                var lines = TextUtil.ParseLrc(SyncedLyrics);
                if (lines.Count > 0)
                    return new LyricsDoc { Lines = lines, Synced = true, Source = "LRCLIB", IsExternal = true };
            }

            if (!string.IsNullOrWhiteSpace(PlainLyrics))
            {
                var lines = PlainLyrics.Split('\n').Select(l => new LyricLine(TimeSpan.Zero, l.Trim())).ToList();
                return new LyricsDoc { Lines = lines, Synced = false, Source = "LRCLIB", IsExternal = true };
            }
            return null;
        }
    }
}
