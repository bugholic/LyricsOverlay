// Usage: node render.js preview 3 10 20 ...   -> PNG stills at those times
//        node render.js video out.mp4          -> full 30fps video via ffmpeg
const { chromium } = require("playwright-core");
const { spawn } = require("child_process");
const path = require("path");

const CHROME = "C:/Users/ayush/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe";
const FPS = 30, DURATION = 57;

(async () => {
  const [mode, ...rest] = process.argv.slice(2);
  const browser = await chromium.launch({ executablePath: CHROME });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  await page.goto("file:///" + path.join(__dirname, "scene.html").replace(/\\/g, "/"));
  await page.evaluate(() => document.fonts.ready);

  if (mode === "preview") {
    for (const t of rest) {
      await page.evaluate(t => render(t), Number(t));
      await page.screenshot({ path: path.join(__dirname, `still_${t}.png`) });
    }
  } else {
    const out = rest[0];
    const ff = spawn("ffmpeg", ["-y", "-f", "image2pipe", "-framerate", String(FPS), "-i", "-",
      "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", out],
      { stdio: ["pipe", "inherit", "inherit"] });
    const total = FPS * DURATION;
    for (let f = 0; f < total; f++) {
      await page.evaluate(t => render(t), f / FPS);
      const buf = await page.screenshot({ type: "jpeg", quality: 95 });
      if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once("drain", r));
      if (f % 150 === 0) console.log(`frame ${f}/${total}`);
    }
    ff.stdin.end();
    await new Promise(r => ff.on("close", r));
  }
  await browser.close();
})();
