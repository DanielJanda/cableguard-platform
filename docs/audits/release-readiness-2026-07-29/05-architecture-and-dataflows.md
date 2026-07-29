# 05 — Architecture and data flows

## LIVE VIDEO (IMPLEMENTED)

```text
Camera RTSP
  → MediaMTX (:8554 ingest, :8889 WHEP, API :9997)
  → Browser WHEP (Monitor /live, kiosk, test-lab)
```

| Item | Value |
|------|-------|
| Processes | MediaMTX service/exe; Chrome |
| Readiness | path `ready=true` + WHEP 201 |
| Failure | tile ERROR/STALE; others isolated if per-tile PC |
| Restart owner | `manage_mediamtx_service` / CC |
| Health | MTX API + Monitor diagnostics overlay |
| Persistent state | none (realtime) |

Evidence: MediaMTX 6 ready paths; Monitor `/live` 200; Gate2 screenshots LIVE.

## DETECTOR VIDEO (IMPLEMENTED on detector PR#7 tip)

```text
Camera or MediaMTX RTSP
  → PyAV reader (preferred) / OpenCV fallback
  → latest-frame slot (PR#5 lineage; reconcile with #7)
  → YOLO+BotSORT+ROI+fall SM
  → optional OpenCV debug window
  → Event Core publisher (opt-in) / jsonl
```

| Failure | stale frames, reconnect, soft-fail if Event Core key missing |
| Restart owner | CC DetectorProcessManager (#34) or CLI |
| Health | runtime_status / heartbeats / CC parser |

## RECORDING (PARTIAL — MediaMTX local; feature branches)

```text
MediaMTX path record=yes
  → runtime/recordings/%path%/*.mp4 (fMP4)
  → playback :9996 localhost
  → recordDeleteAfter cleanup
```

Not an Event Core API. Incident **clip worker** linking events→segments: **NOT IMPLEMENTED**.

## EVENT (IMPLEMENTED)

```text
detector/simulator
  → POST /api/v1/events (X-API-Key)
  → SQLite
  → WS /ws/v1
  → Monitor GlobalAlarmProvider + pages
  → ACK via BFF (+ X-Kiosk-Key)
```

Ports: Event Core typically `:8000`. Health: `/api/v1/health` → `ok` observed 2026-07-29.

## FUTURE / PARTIAL media attachment

```text
event → snapshot → clip job → MTX playback window → incident store → Monitor
```

Status: **PLANNED / NOT IMPLEMENTED** (fields `snapshot_url`/`clip_url` only).

## Doc drift warning

`docs/project/CURRENT_STATE.md` on `main` predates PR #13/#34/#35. Treat this audit pack as newer for freeze decisions until CURRENT_STATE is updated in a dedicated docs PR after merges.
