# Office detector latency audit — 2026-07-28

## Scope

Observed symptom: office-test-camera feels realtime; after starting the office fall detector a delay appears.

Constraints for this audit:

- No fall algorithm / threshold / golden-master / frame-policy changes.
- No automatic detector → relay binding.
- Glass-to-glass (G2G) is never inferred from RTT or inference FPS.

## Architecture (verified in source)

| Path | Source | Role in frontend video? |
|------|--------|-------------------------|
| Monitor WHEP | `VITE_WHEP_BASE_URL` + path `office-test-camera` | **Yes** — exclusive UI video path |
| Detector input | `sites/office/office_test/fall_office_test.yaml` → `rtsp://127.0.0.1:8554/office-test-camera` (`mediamtx_proxy`) | **No** |
| Camera direct RTSP | Not in office test YAML (no camera IP) | **No** |
| Detector debug/processed publish back to MediaMTX | Not present in office test config | **No** |

Conclusion for question “which image is delayed?”:

- **A (clean WHEP in frontend)** and **detector inference** share MediaMTX/GPU but are **not** the same decoded pipeline.
- Frontend must not use OpenCV debug / detector-processed output — and the office-fall page uses `VideoPlayer` / WHEP only.
- Therefore a delay **visible inside the monitor kiosk video** after detector start is either:
  1. transport contention / MediaMTX source pressure,
  2. host GPU/CPU contention affecting WebRTC decode/render,
  3. operator comparing **kiosk WHEP** vs **detector OpenCV debug window** (B), which is a different surface.

## A/B protocol

| Test | Detector | Debug overlay | Intent |
|------|----------|---------------|--------|
| TEST 1 | OFF | n/a | Baseline WHEP |
| TEST 2 | ON | off | Inference load without OpenCV UI |
| TEST 3 | ON | on | Add debug rendering cost |

Metrics required per test (manual G2G via visual cue; never from RTT):

- manual G2G of clean WHEP
- browser received / decoded / rendered FPS
- dropped frames, freeze count, last frame age
- MediaMTX reconnect count
- CPU / RAM / GPU util / VRAM
- detector received FPS, inference FPS
- frame age at inference start, inference duration, frame-to-decision age

## Measurement run (this host, 2026-07-28 afternoon)

### Environment

- Monitor build on `:8080`: branch `feature/office-e2e-fall-test`, route `/test-lab/office-fall` HTTP 200.
- Event Core API reachable (`/api/v1/health` ok) earlier in the session.
- Later in the session: **MediaMTX control API `:9997` unreachable**; WHEP for `office-test-camera` reported `Failed to fetch` / reconnecting on the E2E page.
- Fall office detector process: **not running** during final capture (no `fall_office_test` python process).

### TEST 1 — detector OFF

| Metric | Result |
|--------|--------|
| Manual G2G (WHEP) | **NOT AVAILABLE** (MediaMTX/WHEP down at final measurement; earlier same day WHEP showed LIVE 1280×720 with decoded frames when MTX was up) |
| Browser decoded size | Earlier: 1280×720 LIVE; Final: 0×0 reconnecting |
| Detector FPS / frame age | n/a (OFF) |
| CPU / RAM / GPU (host snapshot) | ~27 GB / 64 GB RAM used; GPU util ~21 %, VRAM ~1.8 / 10 GB (mixed host load) |

### TEST 2 — detector ON without debug

**NOT AVAILABLE** — detector not started in this audit window (would require MediaMTX + office SUB profile live). No algorithm changes performed.

### TEST 3 — detector ON with debug overlay

**NOT AVAILABLE** — same as TEST 2.

## Likely delay cause (ranked)

1. **Comparison surface confusion (B vs A)** — OpenCV debug window / detector capture path can lag while clean WHEP stays closer to realtime. Operators often watch the debug window after “starting detection”.
2. **GPU contention** — YOLO pose on GPU while browser WebRTC also decodes H.264 can raise rendered frame age without changing WHEP endpoint.
3. **OpenCV/FFmpeg capture buffering** on the detector RTSP reader (possible backlog) — **candidate transport bug**, separate fix PR only after TEST 2/3 with `CAP_PROP_BUFFERSIZE` / grab-latest evidence.
4. **MediaMTX source reconnect** when a heavy RTSP reader attaches — check reader count + `readyTime` before/after detector start (not measured this run; API down).

**Ruled out by architecture (unless a future regression appears):**

- Frontend switching to detector-processed stream (office-fall uses WHEP `office-test-camera` only).
- Detector rewriting camera ISP settings at start (office YAML has no camera apply; credentials not in detector config).

## Required follow-up (separate PR if confirmed)

1. Restore MediaMTX; confirm path `office-test-camera` is SUB 102 H.264 720p.
2. Run TEST 1→2→3 with logged WHEP diagnostics + detector runtime_status (`inference_fps`, camera connected).
3. If TEST 2 worsens WHEP last-frame-age without debug UI → profile GPU + MTX readers (contention).
4. If only TEST 3 worsens operator-perceived delay → treat as debug-window cost, not kiosk G2G.
5. If detector `frame age at inference start` climbs while WHEP stays flat → fix capture backlog (buffer size / grab-latest) in a dedicated PR — **do not** touch fall thresholds.

## Frontend endpoint invariance checklist

- [x] Office E2E page WHEP path constant: `office-test-camera`
- [x] Detector RTSP: localhost MediaMTX only
- [x] No camera IP / credentials in monitor office route sources
- [ ] Live A/B numeric table — blocked on MediaMTX availability
