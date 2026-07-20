# CableGuard Platform

Event Core backend for CableGuard: REST API, WebSocket, SQLite history.

Detectors live in the separate repository `cableguard-detector`.  
This project does **not** run YOLO, cameras, USB-4761, or MediaMTX.

## Quick start (Windows)

```powershell
cd cableguard-platform
py -3.10 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e ".[dev]"
Copy-Item .env.example .env
# set CABLEGUARD_INGEST_API_KEY and CABLEGUARD_KIOSK_API_KEY (server-side; not for React/VITE_*)

cd backend
alembic upgrade head
uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

In another terminal:

```powershell
.\scripts\start_backend.ps1   # or run uvicorn as above
python scripts\simulate_system.py --scenario demo
```

Docs: `docs/architecture.md`, `docs/api.md`, `docs/integration-with-ui.md` (ack proxy pattern for `cableguard-monitor`).

## What this is / is not

| Is | Is not |
|---|---|
| Event Core + health + acknowledgements | Fall/safety-bar detection algorithms |
| Local SQLite (WAL) | PostgreSQL / cloud DB |
| Service API key for ingest | Full operator auth (planned before internet exposure) |
| Kiosk API key on Event Core (via server-side proxy for UI ack) | Browser-held kiosk secrets or `VITE_*` keys |
| MediaMTX **example** config only | Running MediaMTX / RTSP |
