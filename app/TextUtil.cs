using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LyricsOverlay;

public static class TextUtil
{
    static readonly Regex NonWord = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    // "(Official Video)", "[Lyrics]", "(Remastered 2011)", "(feat. X)" ... — noise in YouTube titles.
    static readonly Regex Noise = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|lyrics?|audio|visuali[sz]er|hd|4k|mv|m/v|remaster(ed)?|explicit|feat\.?|ft\.?|featuring)\b[^\)\]]*[\)\]]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex TrailingFeat = new(@"\s+(feat\.?|ft\.?|featuring)\s+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex LrcStamp = new(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);

    public static string Normalize(string? s) => NonWord.Replace((s ?? "").ToLowerInvariant(), " ").Trim();

    public static string StripNoise(string s) => TrailingFeat.Replace(Noise.Replace(s, ""), "").Trim();

    /// <summary>Loose title comparison that survives "(Official Video)" suffixes and punctuation.</summary>
    public static bool SameTitle(string a, string b)
    {
        var x = Normalize(StripNoise(a));
        var y = Normalize(StripNoise(b));
        if (x.Length == 0 || y.Length == 0) return false;
        if (x == y) return true;
        return Math.Min(x.Length, y.Length) >= 4 && (x.Contains(y) || y.Contains(x));
    }

    /// <summary>
    /// Turns a media-session title/artist into something a lyrics search understands.
    /// Plain YouTube videos report "Artist - Song (Official Video)" as the title and the channel as artist.
    /// </summary>
    public static (string Title, string Artist) CleanForSearch(string title, string artist)
    {
        var t = StripNoise(title);
        var a = artist.Replace(" - Topic", "", StringComparison.OrdinalIgnoreCase);
        a = Regex.Replace(a, "VEVO$", "", RegexOptions.IgnoreCase).Trim();

        var dash = t.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            a = t[..dash].Trim();
            t = t[(dash + 3)..].Trim();
        }
        return (t, a);
    }

    public static List<LyricLine> ParseLrc(string lrc)
    {
        var result = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var stamps = LrcStamp.Matches(line);
            if (stamps.Count == 0) continue;

            var text = LrcStamp.Replace(line, "").Trim();
            foreach (Match m in stamps)
            {
                var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                result.Add(new LyricLine(TimeSpan.FromSeconds(minutes * 60 + seconds), text));
            }
        }
        result.Sort((p, q) => p.Time.CompareTo(q.Time));
        return result;
    }

    /// <summary>
    /// Plain lyrics have no timestamps, so spread them across the song, leaving a short intro and outro.
    /// It drifts, but keeps the strip moving roughly with the music.
    /// </summary>
    public static List<LyricLine> EstimateTiming(IEnumerable<string> lines, TimeSpan duration)
    {
        var texts = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (duration <= TimeSpan.Zero) duration = TimeSpan.FromMinutes(3.5);

        var start = duration.TotalSeconds * 0.06;
        var span = duration.TotalSeconds * 0.88;
        return texts
            .Select((text, i) => new LyricLine(TimeSpan.FromSeconds(start + span * i / Math.Max(1, texts.Count)), text))
            .ToList();
    }
}
