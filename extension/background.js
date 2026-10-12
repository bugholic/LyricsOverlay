// Relays messages from the player content scripts to the desktop app.
// Fetching from here (not the page) avoids the page's CORS / private-network rules.
const APP = "http://localhost:38917";

chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (!msg || (msg.kind !== "lyrics" && msg.kind !== "state")) return false;

  fetch(`${APP}/${msg.kind}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(msg.payload),
  })
    .then(async (res) => sendResponse({ ok: res.ok, data: res.ok ? await res.json() : null }))
    .catch(() => sendResponse({ ok: false, data: null })); // desktop app isn't running

  return true; // keep the channel open for the async response
});
