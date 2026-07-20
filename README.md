# CableGuard Platform

Event Core backend scaffold (FastAPI + SQLite). Detectors live in `cableguard-detector`.

## Quick start

```powershell
py -3.10 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e ".[dev]"
Copy-Item .env.example .env
.\scripts\start_backend.ps1
```
