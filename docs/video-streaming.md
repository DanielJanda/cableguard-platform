# Video streaming – CableGuard Platform

## Separate pipelines

### Video (this document)

```text
horní kamera Zahrádky
  → RTSP (MediaMTX only)
  → MediaMTX path (zahradky-horni-stanice)
  → WebRTC / WHEP
  → cableguard-monitor (iframe POC → future WHEP VideoPlayer)
```

### Events (Event Core – not part of video)

```text
detektor
  → Event Core (cableguard-platform)
  → SQLite
  → WebSocket / REST
  → cableguard-monitor
```

## Rules

- Frontend **never** receives RTSP URL, camera password, or camera username.
- Event Core does **not** proxy or transcode video.
- MediaMTX performs **no** AI detection.
- Managed scripts in `scripts/start_mediamtx.ps1` track a **separate** local instance (`runtime/mediamtx/mediamtx.pid`).
- Do not stop unrelated legacy Docker MediaMTX without verifying PID/ports.

## Local managed MediaMTX

See `deploy/mediamtx/README.md`.

| Item | Location |
|---|---|
| Example config | `deploy/mediamtx/mediamtx.example.yml` |
| Local secrets config | `deploy/mediamtx/mediamtx.local.yml` (gitignored) |
| Binary | `runtime/mediamtx/mediamtx.exe` (gitignored) |
| Logs / PID | `runtime/mediamtx/` (gitignored) |

## Frontend POC (cableguard-monitor)

Temporary iframe POC uses:

- `VITE_WHEP_BASE_URL` – MediaMTX WebRTC base (e.g. `http://127.0.0.1:8889`)
- `VITE_VIDEO_POC_MODE=iframe`
- `VITE_ZAHRADKY_UPPER_STREAM_NAME=zahradky-horni-stanice`

Production target: native WHEP client in `VideoPlayer` (iframe is diagnostic only).

## Legacy install note

An older Docker-based MediaMTX may exist under `Documents/mediamtx/` with path name **`kamera4`** for the same physical camera. The managed POC uses path **`zahradky-horni-stanice`**. Running both against the same camera creates **two RTSP connections**.

Detectors in `cableguard-detector` currently use **direct RTSP**, not MediaMTX proxy.
