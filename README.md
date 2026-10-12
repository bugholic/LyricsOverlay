# Lyrics Overlay

A Windows tray app that shows lyrics for whatever music is playing in your browser, as a karaoke
strip that floats over everything. The strip ignores the mouse and never takes focus, so it never
gets in the way. It becomes interactive only when you choose **Move / resize overlay**.

## How it works

```
 Browser (YouTube Music, Spotify Web, YouTube…)
   │  title / artist / position            │  lyrics panel + exact position
   ▼  (Windows media sessions)             ▼  (Lyrics Overlay Bridge extension → localhost:38917)
 ┌──────────────────────── LyricsOverlay.exe ────────────────────────┐
 │ 1. Lyrics from the player page        (always first)              │
 │ 2. LRCLIB.net, time-synced            (only if you allow it)      │
 │ → click-through karaoke strip, tray icon, settings window         │
 └───────────────────────────────────────────────────────────────────┘
```

- **Detecting the song** needs nothing extra. Chrome, Edge, Firefox, Brave, Opera and Vivaldi
  publish what's playing to Windows (it's what the volume flyout's media controls show).
- **Player lyrics** come from the browser extension. It reads YouTube Music's *Lyrics* tab. If
  the tab hasn't been opened for a song, the extension opens it for a moment and then switches back.
- **External lyrics** come from [LRCLIB](https://lrclib.net), which is free, needs no key, and has
  time-synced lyrics. This is **off until you turn it on** in Settings. When on, only the title,
  artist, album and length are sent.
- YouTube Music's lyrics are plain text without timestamps, so the strip spreads them across the
  song length. That's approximate. Turn on *Prefer LRCLIB's time-synced lyrics* if you want exact
  timing whenever LRCLIB has it.

## Build and run

Requires the .NET 9 SDK and Windows 10 (2004) or later.

```powershell
cd app
dotnet build -c Release
.\bin\Release\net9.0-windows10.0.19041.0\LyricsOverlay.exe
```

For a single self-contained exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

On first launch, the Settings window opens so you can decide about external lyrics. After that the
app sits in the tray (♪ icon): double-click it for Settings, or right-click it for the menu.

## Install the browser extension (Chrome / Edge)

1. Open `chrome://extensions` (or `edge://extensions`).
2. Turn on **Developer mode**.
3. Click **Load unpacked** and pick the `extension` folder.
4. Reload any open music.youtube.com tab.

Settings shows "✔ Browser extension connected" once it's talking to the app.

## Using it

| Want to… | Do this |
|---|---|
| Move or resize the strip | Tray → **Move / resize overlay**. Drag to move, drag the corner to resize, then **Done** or Esc |
| Change text size | Settings slider, or **A− / A+** while moving |
| Hide lyrics | Tray → uncheck **Show lyrics** |
| Allow internet lyrics | Settings → **Allow external lyrics from LRCLIB.net** |
| Start with Windows | Settings → **Start Lyrics Overlay when I sign in** |

Settings are saved in `%APPDATA%\LyricsOverlay\settings.json`.

## Project layout

```
app/
  App.xaml.cs            startup, tray wiring, single instance
  MediaWatcher.cs        Windows media sessions → current track + position
  ExtensionBridge.cs     localhost:38917 endpoint for the extension (only accepts extension origins)
  LrcLibClient.cs        LRCLIB lookup (exact match, then search)
  LyricsController.cs    picks player vs. external lyrics, tracks playback position
  OverlayWindow.xaml     the click-through karaoke strip
  SettingsWindow.xaml    settings UI
  TrayIcon.cs, Native.cs tray icon, click-through window styles
extension/
  manifest.json, background.js, ytmusic.js
```

## Known limits

- The YouTube Music lyrics reader depends on YouTube Music's page structure. If YouTube changes
  it, update the selectors in `extension/ytmusic.js` (`readLyrics` and `tabs`).
- To add another web player, write a content script like `ytmusic.js` that posts to `/lyrics` and
  `/state`, and add its URL to `manifest.json`.
- The overlay stays on top of normal windows, but exclusive-fullscreen games can still cover it.
- The extension is Chrome/Edge (Manifest V3) only. Firefox would need a small manifest change.

## Landing page

`site/` is a static landing page, deployable to Netlify as-is (`netlify.toml` publishes `site/`, no build).
Put the usability video at `site/demo.mp4` (optional `site/poster.jpg`), or set `data-youtube` on the
`#video` element in `site/index.html` to a YouTube video ID.

The demo video is generated: `tools/demo-video/scene.html` is an animated mock of the app, and
`npm install && npm run render` in that folder renders it to `site/demo.mp4` (needs ffmpeg on PATH;
set the Chromium path at the top of `render.js`).
