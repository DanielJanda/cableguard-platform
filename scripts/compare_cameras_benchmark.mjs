/**
 * Dual-camera WHEP benchmark with persistent sessions (Edge / Playwright).
 * Run from cableguard-monitor (playwright dependency):
 *   node ../cableguard-platform/scripts/compare_cameras_benchmark.mjs --minutes=20
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const __dirname = dirname(fileURLToPath(import.meta.url));
const monitorRoot = join(__dirname, "..", "..", "cableguard-monitor");
const playwrightEntry = join(monitorRoot, "node_modules", "playwright", "index.mjs");
try {
  require.resolve(playwrightEntry);
} catch {
  console.error("Install playwright in cableguard-monitor: npm install -D playwright");
  process.exit(1);
}
const { chromium } = await import(pathToFileURL(playwrightEntry).href);

const LAN = "10.6.1.40";
const ORIGIN = `http://${LAN}:8080`;
const WHEP_BASE = `http://${LAN}:8889`;
const STREAMS = [
  { id: "camera92", ip: "10.2.4.92", name: "zahradky-horni-stanice-92" },
  { id: "camera90", ip: "10.2.4.90", name: "zahradky-horni-stanice-90" },
];

const durationMin = Number(process.argv.find((a) => a.startsWith("--minutes="))?.split("=")[1] ?? "20");
const outPath =
  process.argv.find((a) => a.startsWith("--out="))?.split("=")[1] ??
  join("runtime", "compare-benchmark.json");

async function main() {
  mkdirSync(dirname(outPath), { recursive: true });
  const browser = await chromium.launch({ headless: true, channel: "msedge" });
  const page = await browser.newPage();
  await page.goto(`${ORIGIN}/dashboard`, { waitUntil: "domcontentloaded", timeout: 60000 });

  const setup = await page.evaluate(
    async ({ origin, whepBase, streams }) => {
      async function connectOne(streamName) {
        const t0 = performance.now();
        const whepUrl = `${whepBase}/${streamName}/whep`;
        const opt = await fetch(whepUrl, { method: "OPTIONS", headers: { Origin: origin } });
        const pc = new RTCPeerConnection({ iceServers: [] });
        pc.addTransceiver("video", { direction: "recvonly" });
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        await new Promise((r) => {
          if (pc.iceGatheringState === "complete") r();
          else {
            pc.onicegatheringstatechange = () => pc.iceGatheringState === "complete" && r();
            setTimeout(r, 4000);
          }
        });
        const post = await fetch(whepUrl, {
          method: "POST",
          headers: { Origin: origin, "Content-Type": "application/sdp" },
          body: pc.localDescription?.sdp ?? "",
        });
        const answer = await post.text();
        const loc = post.headers.get("location");
        const etag = post.headers.get("etag");
        let patchStatus = null;
        if (loc && etag) {
          const ru = loc.startsWith("http") ? loc : `${whepBase}${loc}`;
          patchStatus = (
            await fetch(ru, {
              method: "PATCH",
              headers: {
                Origin: origin,
                "Content-Type": "application/trickle-ice-sdpfrag",
                "If-Match": etag,
              },
              body: "a=ice-options:trickle\r\n",
            })
          ).status;
        }
        if (post.ok) await pc.setRemoteDescription({ type: "answer", sdp: answer });
        const v = document.createElement("video");
        v.autoplay = true;
        v.muted = true;
        v.playsInline = true;
        const ms = new MediaStream();
        v.srcObject = ms;
        pc.ontrack = (e) => ms.addTrack(e.track);
        document.body.appendChild(v);
        await v.play().catch(() => {});

        let firstFrameMs = null;
        for (let i = 0; i < 48; i++) {
          await new Promise((r) => setTimeout(r, 250));
          if (v.videoWidth > 0 && firstFrameMs == null) firstFrameMs = performance.now() - t0;
          if (pc.iceConnectionState === "connected" && v.videoWidth > 0) break;
        }

        return {
          streamName,
          optionsStatus: opt.status,
          postStatus: post.status,
          patchStatus,
          ice: pc.iceConnectionState,
          firstFrameMs,
          pc,
          video: v,
        };
      }

      const sessions = {};
      for (const s of streams) {
        sessions[s.id] = await connectOne(s.name);
      }

      window.__cgBench = { sessions, streams };

      async function sample(id) {
        const sess = window.__cgBench.sessions[id];
        const rx = sess.pc.getReceivers().find((r) => r.track?.kind === "video");
        let framesReceived = 0;
        let bytesReceived = 0;
        let fps = null;
        if (rx) {
          const stats = await rx.getStats();
          for (const x of stats.values()) {
            if (x.type === "inbound-rtp" && x.kind === "video") {
              framesReceived = x.framesReceived ?? 0;
              bytesReceived = x.bytesReceived ?? 0;
              if (x.framesPerSecond != null) fps = Math.round(x.framesPerSecond);
            }
          }
        }
        return {
          streamName: sess.streamName,
          ice: sess.pc.iceConnectionState,
          width: sess.video.videoWidth,
          height: sess.video.videoHeight,
          framesReceived,
          bytesReceived,
          fps,
        };
      }

      window.__cgBenchSample = sample;
      const handshake = {};
      for (const s of streams) {
        const sess = sessions[s.id];
        handshake[s.id] = {
          streamName: sess.streamName,
          optionsStatus: sess.optionsStatus,
          postStatus: sess.postStatus,
          patchStatus: sess.patchStatus,
          ice: sess.ice,
          firstFrameMs: sess.firstFrameMs,
        };
      }
      return handshake;
    },
    { origin: ORIGIN, whepBase: WHEP_BASE, streams: STREAMS },
  );

  const startedAt = new Date().toISOString();
  const samples = [];
  const intervalSec = 30;
  const endAt = Date.now() + durationMin * 60 * 1000;
  let tick = 0;
  let prevFrames = { camera92: 0, camera90: 0 };
  let freezes = { camera92: 0, camera90: 0 };

  while (Date.now() < endAt) {
    tick += 1;
    const row = { tick, at: new Date().toISOString(), streams: {} };
    for (const s of STREAMS) {
      const sample = await page.evaluate(async (id) => window.__cgBenchSample(id), s.id);
      if (sample.framesReceived <= prevFrames[s.id]) freezes[s.id] += 1;
      prevFrames[s.id] = sample.framesReceived;
      row.streams[s.id] = { ...sample, ip: s.ip, freezeTicks: freezes[s.id] };
    }
    samples.push(row);
    console.log(
      `[${tick}] 92=${row.streams.camera92.framesReceived}f/${row.streams.camera92.ice} ` +
        `90=${row.streams.camera90.framesReceived}f/${row.streams.camera90.ice}`,
    );
    if (Date.now() < endAt) await new Promise((r) => setTimeout(r, intervalSec * 1000));
  }

  await browser.close();

  const report = {
    startedAt,
    finishedAt: new Date().toISOString(),
    durationMin,
    handshake: setup,
    samples,
    summary: {
      freezeTicks: freezes,
      note: "Compare visible clock/motion between feeds for latency; no unsafe scenarios triggered.",
    },
  };
  writeFileSync(outPath, JSON.stringify(report, null, 2));
  console.log(`Wrote ${outPath}`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
