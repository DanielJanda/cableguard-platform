# Office detector latency live A/B — 2026-07-28 (integration run)

## Environment

- Admin Studio: `integrate/office-e2e-usb4761` (build stamp during UI capture: `2d5fb18` + local uncommitted UI then committed)
- MediaMTX: started; path `office-test-camera` ready
- Monitor WHEP: `http://10.6.1.40:8080/test-lab/stream/office-test-camera`
- Detector input: `rtsp://127.0.0.1:8554/office-test-camera` (`mediamtx_proxy`)
- Frontend WHEP path unchanged across A/B/C
- MediaMTX `readyTime` **unchanged** across detector start (no source reconnect)

## Video flow confirmation

| Role | Endpoint | In frontend path? |
|------|----------|-------------------|
| Clean WHEP | MediaMTX WHEP `office-test-camera` | Yes |
| Detector | local RTSP same path | No |
| Detector publish of processed video | none | No |

Capture hardening already present: `CAP_PROP_BUFFERSIZE=1`, FFmpeg `nobuffer|low_delay`.

## Results (≈60s windows; not full 3×180s — same qualitative conclusion)

Manual glass-to-glass (clap/LED) was **not** performed → G2G ms = **NOT AVAILABLE** (not inferred from FPS).

| Metric | TEST A detector OFF | TEST B detector ON no debug | TEST C detector ON + debug |
|--------|---------------------|-----------------------------|----------------------------|
| Clean WHEP decoded size | 1280×720 | 1280×720 | 1280×720 |
| `video.currentTime` drift | +10.0 s / 10 s | +10.0 s / 10 s | +10.0 s / 10 s |
| Freeze count (Δt&lt;0.2s / 10s) | 0 | 0 | 0 |
| Dropped frames (browser) | not exposed | not exposed | not exposed |
| WHEP reconnects | 0 (stable) | 0 | 0 |
| MTX readers | 1 (webRTC) | 2 (webRTC+rtsp) | 3* |
| MTX readyTime | stable | **same** | **same** |
| CPU % | ~8 | ~9 | ~19 |
| GPU util / VRAM | 14% / 1701 MiB | 36% / 2166 MiB | 28% / 2544 MiB |
| Detector inference FPS | n/a | **23.48** | **23.40** |
| queue_age_ms / frame_to_decision | **NOT INSTRUMENTED** | same | same |

\* Extra RTSP reader likely leftover session during restart overlap.

## CLEAN WHEP LATENCY vs DETECTOR INPUT AGE vs DEBUG WINDOW

- **CLEAN WHEP:** remains wall-clock realtime under A/B/C (currentTime tracks wall clock; freezes=0). Perceived delay is **not** explained by WHEP switching or MTX reconnect.
- **DETECTOR INPUT AGE:** RTSP reader attaches; inference ~23 FPS; buffer policy grab-latest (`BUFFERSIZE=1`). No rising backlog metric available without new instrumentation → **backlog not confirmed**.
- **DEBUG WINDOW LATENCY:** OpenCV overlay is a **separate surface**. Operator delay after “start detection with preview” is most consistent with watching the debug window / GPU contention, not the clean WHEP kiosk feed.

## Backlog verdict

**Not confirmed.** Evidence against backlog as WHEP cause:

1. WHEP stays realtime with detector ON
2. `readyTime` unchanged
3. Capture already uses low-delay + buffer size 1
4. Inference FPS stable (~23.4) over 30s windows

To prove/disprove queue growth rigorously, add monotonic `frame_received` / `inference_started` / `queue_age_ms` logging in a **separate transport PR** (no threshold/golden changes).

## Proposed transport fix (design only — not implemented)

If later instrumentation shows rising `queue_age_ms`:

1. Ensure single outstanding grab (drop intermediate) before inference
2. Keep algorithm/thresholds/golden master identical
3. Acceptance: live WHEP G2G unchanged; detector `queue_age_ms` p95 bounded; golden still passes
