# Local Event Core + Monitor integration

Full local stack for CableGuard Monitor connected to Event Core (no detector, no camera AI).

## Architecture

```text
simulátor (scripts/simulate_system.py)
  → Event Core REST (ingest)
  → SQLite
  → REST + WebSocket
  → cableguard-monitor

Video (parallel, unchanged):
  kamera → MediaMTX → WebRTC → monitor iframe POC
```

## Prerequisites

- Python venv in `cableguard-platform` (`.venv`)
- Node.js in PATH
- `cableguard-platform/.env` with explicit non-placeholder `CABLEGUARD_INGEST_API_KEY` and `CABLEGUARD_KIOSK_API_KEY`
- `cableguard-monitor/.env.local` (gitignored) – kiosk key must match platform `.env` (see below)
- Optional: managed MediaMTX for live video (`scripts/start_mediamtx.ps1`)

## Quick start

From repo root `cableguard-platform`:

```powershell
.\scripts\start_local_demo.ps1
```

Or step by step:

### 1. Event Core

```powershell
cd C:\Users\mega\Documents\cableguard-platform
$env:PYTHONPATH = "$PWD\backend"
cd backend
..\.venv\Scripts\alembic.exe upgrade head
cd ..
.\.venv\Scripts\uvicorn.exe app.main:app --app-dir backend --host 127.0.0.1 --port 8000
```

First run requires Alembic migrations (SQLite schema).

### 2. MediaMTX (optional video)

```powershell
.\scripts\start_mediamtx.ps1
.\scripts\status_mediamtx.ps1
```

### 3. Monitor

```powershell
cd C:\Users\mega\Documents\cableguard-monitor
npm run dev
```

Open: http://127.0.0.1:5173/kiosk/zahradky/horni-stanice

### 4. Simulator scenarios

```powershell
cd C:\Users\mega\Documents\cableguard-platform
$env:PYTHONPATH = "$PWD\backend"
python scripts\simulate_system.py --scenario heartbeats
python scripts\simulate_system.py --scenario fall
python scripts\simulate_system.py --scenario bar
python scripts\simulate_system.py --scenario camera_offline
python scripts\simulate_system.py --scenario io_fault
python scripts\simulate_system.py --scenario demo
```

## Vite BFF proxy

Monitor dev server exposes:

- Browser: `POST /bff/events/{event_id}/acknowledge`
- Proxy forwards to: `POST http://127.0.0.1:8000/api/v1/events/{event_id}/acknowledge`
- Server injects `X-Kiosk-Key` from `CABLEGUARD_KIOSK_API_KEY` (never in browser bundle)

## Development database reset

Local SQLite only (`data/cableguard.sqlite3`). **Never use on production.**

```powershell
# Stop Event Core first (port 8000 must be free)
.\scripts\reset_development_database.ps1              # dry-run – no changes
.\scripts\reset_development_database.ps1 -ConfirmReset  # backup + wipe + alembic upgrade head
```

- Backups: `runtime/backups/cableguard-<timestamp>.sqlite3` (gitignored)
- Does not seed simulator events
- Refuses network paths and paths outside the repo

## Security

- Do not commit `.env`, `mediamtx.local.yml`, or monitor `.env.local`
- Set explicit non-placeholder `CABLEGUARD_INGEST_API_KEY` and `CABLEGUARD_KIOSK_API_KEY` in platform `.env`
- Kiosk key must match monitor `.env.local` (`CABLEGUARD_KIOSK_API_KEY`, no `VITE_` prefix)
- Frontend never receives ingest or kiosk API keys
- Published Lovable app stays in mock mode without local Vite BFF proxy

## Known limitation

MediaMTX POC may use camera `10.2.4.92` (substream) while `cableguard-detector` `.env` may reference a different RTSP source. Detector is unchanged in this integration; unify RTSP in a later task.
