# Gate 1 — MediaMTX v1.11.3 rolling recording (office-test-camera)

**Date:** 2026-07-29  
**Branch:** `feat/incident-pipeline-mvp` (stacked on platform PR #32 / `feat/pyav-detector-runtime`)  
**Binary:** `runtime/mediamtx/mediamtx.exe` → `v1.11.3`

## Capability audit (local executable + upstream `v1.11.3` mediamtx.yml / path.go)

| Funkce | v1.11.3 dostupná | Ověřeno jak |
|---|---|---|
| Native recording (`record`) | YES | binary JSON tags + live API patch + disk segments |
| fMP4 (`recordFormat: fmp4`) | YES | binary + ffprobe of segments (H.264 inside fMP4) |
| Automatic deletion (`recordDeleteAfter`) | YES | schema + Gate 1 deletion watch |
| Playback list (`GET /list`) | YES | binary + live `127.0.0.1:9996/list` |
| Playback get (`GET /get`) | YES | live download HTTP 200 |
| MP4 export (`format=mp4`) | YES | `/get?...&format=mp4` → duration≈30.01 s |
| Segment hooks (`runOnRecordSegmentCreate/Complete`) | YES | present in binary + v1.11.3 path.go (not used in Gate 1) |
| `recordMaxPartSize` | **NO** | absent from binary JSON tags and v1.11.3 `Path` struct |

**Note:** Part size is time-bounded via `recordPartDuration` (Gate 1: `1s`). No MediaMTX upgrade required for Gate 1.

## Isolation

- Recording enabled **only** on `office-test-camera`
- `zahradky-horni-stanice` / `-90` / `-92` remain `record=false`
- Playback bound to `127.0.0.1:9996` (not `0.0.0.0`)
- Config persisted in gitignored `deploy/mediamtx/mediamtx.local.yml`
- Example overlay: `deploy/mediamtx/mediamtx.recording.office.example.yml`
- Helpers: `scripts/enable_office_rolling_recording.ps1`, `scripts/status_office_recording.ps1`

## Observed metrics (Gate 1 — 2026-07-29)

| Metrika | Výsledek |
|---|---:|
| Recording status | ENABLED only on `office-test-camera` |
| Codec | H.264 1280×720 (no re-encode; fMP4 container) |
| Segment duration | 30 s |
| Part duration | 1 s |
| Segment count (steady) | ~21 (≈10 min window) |
| Cache size | ~147 MB |
| Oldest / newest | within ≤10 min after cleaner tick |
| Native deletion | YES — `recordDeleteAfter=10m`; cleaner interval ≈ `deleteAfter/2` (5 m) |
| Playback 30 s | HTTP 200, duration ≈30.01 s, H.264, ~7.6 MB |
| Playback bind | `127.0.0.1:9996` only |
| WHEP status | OPTIONS 204 |
| PyAV detector status | OK (30 frames decode from `rtsp://127.0.0.1:8554/office-test-camera`) |
| MediaMTX RSS | ~85→88 MB during record; no runaway growth |
| Zahrádky recording | OFF (`record=false` on all zahradky paths) |

**Cleaner note:** first check at age 10.9 min can still show old files until the next cleaner tick (~5 min). After tick, `old10m=0`.
