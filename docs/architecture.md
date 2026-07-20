# Architecture – CableGuard Platform

## Role

`cableguard-platform` is the **Event Core**:

- receives events and heartbeats from detectors / IO agent,
- stores history in local SQLite,
- exposes REST + WebSocket for kiosk / dashboard UI.

`cableguard-detector` remains a separate repository (YOLO, USB, cameras).

## Data flow (target)

```text
Detectors / IO / MediaMTX health
  → REST ingest (API key)
  → Event Core (this repo)
  → SQLite
  → WebSocket + REST reads
  → Lovable / React UI (future)

Cameras → MediaMTX → WHEP → UI VideoPlayer (not via Event Core)
```

## Hard rules

- Event Core / frontend **must not** drive USB-4761 or the physical semaphore.
- UI never opens SQLite, RTSP, or USB directly.
- Ingest uses a **service API key** (`X-API-Key`). Do not log its value.
- Acknowledgements use a separate **kiosk API key** (`X-Kiosk-Key`).
- `GET` and WebSocket are for trusted local use only until operator auth or a trusted proxy is added.
- Before exposing to the public internet: add operator authentication or a trusted reverse proxy.

## Components

| Piece | Tech |
|---|---|
| API | FastAPI |
| DB | SQLite + WAL, SQLAlchemy 2, Alembic |
| Realtime | WebSocket `/ws/v1` |
| Simulator | `scripts/simulate_system.py` |
