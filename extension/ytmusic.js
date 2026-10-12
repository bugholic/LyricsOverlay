// Reads the lyrics YouTube Music already shows (its "Lyrics" tab) and sends them, plus the
// playback position, to the Lyrics Overlay desktop app through background.js.
(() => {
  const SOURCE = "YouTube Music";

  // YouTube Music only loads lyrics once the Lyrics tab is opened. When true, the tab is opened
  // briefly for each new song and then switched back to the tab you were on.
  const AUTO_OPEN_LYRICS_TAB = true;

  let trackKey = null;
  let lyricsPayload = null; // last lyrics message for the current song
  let currentText = null; // lyrics text of the current song
  let previousText = null; // lyrics text of the previous song, to spot a stale panel
  let probing = false;

  const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

  function send(kind, payload) {
    try {
      return chrome.runtime.sendMessage({ kind, payload }).catch(() => null);
    } catch {
      return Promise.resolve(null); // extension was reloaded; this tab needs a refresh
    }
  }

  function currentTrack() {
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    const title =
      (md && md.title) ||
      document.querySelector("ytmusic-player-bar .title")?.textContent?.trim() ||
      "";
    let artist = (md && md.artist) || "";
    if (!artist) {
      const byline = document.querySelector("ytmusic-player-bar .byline")?.textContent || "";
      artist = byline.split("•")[0].trim();
    }
    return { title, artist };
  }

  function tabs() {
    const all = [...document.querySelectorAll("ytmusic-player-page tp-yt-paper-tab")];
    // Tabs are "Up next / Lyrics / Related"; match by text, fall back to position for other languages.
    const lyrics = all.find((t) => /lyric/i.test(t.textContent)) || all[1] || null;
    const selected = all.find(
      (t) => t.classList.contains("iron-selected") || t.getAttribute("aria-selected") === "true"
    );
    const disabled =
      !!lyrics && (lyrics.hasAttribute("disabled") || lyrics.getAttribute("aria-disabled") === "true");
    return { lyrics, selected, disabled };
  }

  // "found" (with text), "none" (player says no lyrics), or "unloaded" (can't tell yet).
  function readLyrics() {
    const panel = document.querySelector(
      'ytmusic-tab-renderer[page-type="MUSIC_PAGE_TYPE_TRACK_LYRICS"]'
    );
    if (!panel) return { state: "unloaded" };
    const text = panel
      .querySelector("ytmusic-description-shelf-renderer .description")
      ?.innerText?.trim();
    if (text && text !== previousText) return { state: "found", text };
    if (!text && panel.querySelector("ytmusic-message-renderer")) return { state: "none" };
    return { state: "unloaded" };
  }

  function publish(track, result) {
    const found = result.state === "found";
    if (found) currentText = result.text;
    lyricsPayload = {
      title: track.title,
      artist: track.artist,
      source: SOURCE,
      found,
      synced: false,
      lines: found
        ? result.text
            .split("\n")
            .map((s) => s.trim())
            .filter(Boolean)
            .map((text) => ({ text }))
        : [],
    };
    send("lyrics", lyricsPayload);
  }

  async function probe(key, track) {
    if (probing) return;
    probing = true;
    try {
      await sleep(1500); // let the player page catch up with the new song
      if (key !== trackKey) return;

      let result = readLyrics();
      const { lyrics, selected, disabled } = tabs();

      if (result.state === "unloaded" && lyrics && !disabled && AUTO_OPEN_LYRICS_TAB) {
        if (selected !== lyrics) lyrics.click();
        for (let i = 0; i < 20 && (result = readLyrics()).state === "unloaded"; i++) await sleep(250);
        if (selected && selected !== lyrics) selected.click();
      }
      if (key !== trackKey) return;

      if (disabled && result.state !== "found") result = { state: "none" };
      if (result.state !== "unloaded") publish(track, result);
      // Still "unloaded": the heartbeat keeps checking in case lyrics show up later.
    } finally {
      probing = false;
      if (key !== trackKey && trackKey) probe(trackKey, currentTrack());
    }
  }

  setInterval(async () => {
    const track = currentTrack();
    const video = document.querySelector("video");
    if (!track.title || !video) return;

    const key = `${track.title}\u0000${track.artist}`;
    if (key !== trackKey) {
      trackKey = key;
      if (currentText) previousText = currentText;
      currentText = null;
      lyricsPayload = null;
      probe(key, track);
    } else if (!(lyricsPayload && lyricsPayload.found) && !probing) {
      // Lyrics can show up later, e.g. when you open the Lyrics tab yourself.
      const result = readLyrics();
      if (result.state === "found") publish(track, result);
    }

    const res = await send("state", {
      title: track.title,
      artist: track.artist,
      source: SOURCE,
      positionMs: Math.round(video.currentTime * 1000),
      playing: !video.paused,
    });
    // The app restarted or missed it: send the lyrics we already have again.
    if (res && res.data && res.data.wantLyrics && lyricsPayload) send("lyrics", lyricsPayload);
  }, 1000);
})();
