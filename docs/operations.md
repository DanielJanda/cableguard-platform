# Operations

## Local start

```powershell
cd <PROJECT_ROOT>
py -3.10 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e ".[dev]"
Copy-Item .env.example .env
# edit CABLEGUARD_INGEST_API_KEY and CABLEGUARD_KIOSK_API_KEY

.\scripts\reset_development_database.ps1
.\scripts\start_backend.ps1
```

Simulator:

```powershell
$env:PYTHONPATH = "<PROJECT_ROOT>\backend"
python scripts\simulate_system.py --scenario demo
```

## CORS

`CABLEGUARD_CORS_ORIGINS` – comma-separated. Default local Vite origins only.

## Security notes

- Never commit `.env` or the SQLite file.
- Never log ingest or kiosk API keys.
- `GET` and WebSocket are for trusted local use only until operator auth or a trusted proxy is added.
- Acknowledgements trust the client-supplied `acknowledged_by` / `kiosk_id` as audit metadata; cryptographic operator identity is **not** implemented yet.
- Do not expose this API on the public internet without additional authentication.
