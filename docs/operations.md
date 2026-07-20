# Operations

## Local start

```powershell
cd C:\Users\mega\Documents\cableguard-platform
py -3.10 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e ".[dev]"
Copy-Item .env.example .env
# edit CABLEGUARD_INGEST_API_KEY

.\scripts\reset_development_database.ps1
.\scripts\start_backend.ps1
```

Simulator:

```powershell
$env:PYTHONPATH = "C:\Users\mega\Documents\cableguard-platform\backend"
python scripts\simulate_system.py --scenario demo
```

## CORS

`CABLEGUARD_CORS_ORIGINS` – comma-separated. Default local Vite origins only.

## Security notes

- Never commit `.env` or the SQLite file.
- Never log the ingest API key.
- Operator login is **not** implemented yet. Do not expose this API on the public internet without auth or a trusted proxy.
- Acknowledgements currently trust the client-supplied `acknowledged_by` / `kiosk_id`.
