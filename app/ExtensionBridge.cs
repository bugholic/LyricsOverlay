using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LyricsOverlay;

/// <summary>
/// Tiny localhost HTTP endpoint the browser extension posts to:
///   GET  /ping    → health check
///   POST /lyrics  → lyrics the player page shows (or found=false when it has none)
///   POST /state   → title + playback position heartbeat; reply says whether we still want lyrics
/// Only requests from browser extensions (or with no Origin, e.g. curl) are accepted, so web pages can't post.
/// </summary>
public sealed class ExtensionBridge : IDisposable
{
    public const int Port = 38917;
    const int MaxBody = 512 * 1024;

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    readonly HttpListener _listener = new();
    readonly Dispatcher _ui;
    readonly HashSet<string> _titlesWithAnswer = new();
    DateTime _lastContactUtc = DateTime.MinValue;

    public event Action<PlayerLyrics>? LyricsReceived;
    public event Action<PlayerState>? StateReceived;

    public bool IsListening => _listener.IsListening;
    public bool IsConnected => DateTime.UtcNow - _lastContactUtc < TimeSpan.FromSeconds(6);
    public string? LastSource { get; private set; }

    public ExtensionBridge(Dispatcher ui) => _ui = ui;

    public void Start()
    {
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            return; // Port in use: run without the extension rather than crash.
        }
        _ = Task.Run(AcceptLoopAsync);
    }

    async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            var origin = req.Headers["Origin"];
            if (origin is not null &&
                !origin.StartsWith("chrome-extension://", StringComparison.Ordinal) &&
                !origin.StartsWith("moz-extension://", StringComparison.Ordinal))
            {
                res.StatusCode = 403;
                return;
            }
            if (origin is not null) res.AddHeader("Access-Control-Allow-Origin", origin);

            if (req.HttpMethod == "OPTIONS")
            {
                res.AddHeader("Access-Control-Allow-Methods", "GET, POST");
                res.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                res.StatusCode = 204;
                return;
            }

            var path = req.Url?.AbsolutePath ?? "";
            if (req.HttpMethod == "GET" && path == "/ping")
            {
                WriteJson(res, new { app = "LyricsOverlay" });
                return;
            }
            if (req.HttpMethod != "POST" || (path != "/lyrics" && path != "/state"))
            {
                res.StatusCode = 404;
                return;
            }

            var body = ReadBody(req);
            if (body is null)
            {
                res.StatusCode = 413;
                return;
            }
            var dto = JsonSerializer.Deserialize<IncomingDto>(body, Json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
            {
                res.StatusCode = 400;
                return;
            }

            _lastContactUtc = DateTime.UtcNow;
            LastSource = dto.Source;
            var titleKey = TextUtil.Normalize(dto.Title);

            if (path == "/lyrics")
            {
                lock (_titlesWithAnswer) _titlesWithAnswer.Add(titleKey);
                var lyrics = new PlayerLyrics(dto.Title, dto.Artist ?? "", ToDoc(dto), DateTime.UtcNow);
                _ui.BeginInvoke(() => LyricsReceived?.Invoke(lyrics));
                WriteJson(res, new { ok = true });
            }
            else
            {
                var state = new PlayerState(dto.Title, dto.Artist ?? "", dto.Source ?? "",
                    TimeSpan.FromMilliseconds(Math.Max(0, dto.PositionMs ?? 0)), dto.Playing, DateTime.UtcNow);
                _ui.BeginInvoke(() => StateReceived?.Invoke(state));
                bool want;
                lock (_titlesWithAnswer) want = !_titlesWithAnswer.Contains(titleKey);
                WriteJson(res, new { ok = true, wantLyrics = want });
            }
        }
        catch (JsonException)
        {
            res.StatusCode = 400;
        }
        catch (Exception)
        {
            res.StatusCode = 500;
        }
        finally
        {
            try { res.Close(); } catch (Exception) { /* client went away */ }
        }
    }

    static LyricsDoc? ToDoc(IncomingDto dto)
    {
        if (!dto.Found || dto.Lines is null) return null;
        var lines = dto.Lines
            .Where(l => l.Text is not null)
            .Select(l => new LyricLine(TimeSpan.FromMilliseconds(l.TimeMs ?? 0), l.Text!.Trim()))
            .ToList();
        if (lines.Count == 0) return null;
        var synced = dto.Synced && dto.Lines.All(l => l.TimeMs is not null);
        return new LyricsDoc { Lines = lines, Synced = synced, Source = dto.Source ?? "Player" };
    }

    static string? ReadBody(HttpListenerRequest req)
    {
        if (req.ContentLength64 > MaxBody) return null;
        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
        var buffer = new char[MaxBody + 1];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return read > MaxBody ? null : new string(buffer, 0, read);
    }

    static void WriteJson(HttpListenerResponse res, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        res.ContentType = "application/json";
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes);
    }

    public void Dispose()
    {
        try { _listener.Close(); } catch (Exception) { /* already closed */ }
    }

    sealed class IncomingDto
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Source { get; set; }
        public bool Found { get; set; }
        public bool Synced { get; set; }
        public List<LineDto>? Lines { get; set; }
        public double? PositionMs { get; set; }
        public bool Playing { get; set; }
    }

    sealed class LineDto
    {
        public double? TimeMs { get; set; }
        public string? Text { get; set; }
    }
}
