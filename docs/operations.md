# Operations

## Local start

```powershell
cd <PROJECT_ROOT>
py -3.10 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e ".[dev]"
Copy-Item .env.example .env
# edit CABLEGUARD_INGEST_API_KEY and CABLEGUARD_KIOSK_API_KEY (server-side only)

.\scripts\reset_development_database.ps1
.\scripts\start_backend.ps1
```

Simulator (server-side tools — may read keys from platform `.env`):

```powershell
$env:PYTHONPATH = "<PROJECT_ROOT>\backend"
python scripts\simulate_system.py --scenario demo
```

## Kiosk key and the React UI

`CABLEGUARD_KIOSK_API_KEY` in platform `.env` is **Event Core / proxy server configuration**. It is not a frontend variable.

| OK | Not OK |
|---|---|
| Platform `.env`, proxy env, shell env for Vite dev server | `VITE_CABLEGUARD_KIOSK_API_KEY` or any `VITE_*` secret |
| Proxy injects `X-Kiosk-Key` on forward | React `fetch(..., { headers: { "X-Kiosk-Key": ... } })` |
| `cableguard-monitor` Vite dev proxy (future) | Static lovable.app bundle holding the key |

The browser never sees the kiosk key. Configure the Vite proxy in `cableguard-monitor` (see `docs/integration-with-ui.md`). For production, use Caddy, Nginx, or a local BFF the same way.

## CORS

`CABLEGUARD_CORS_ORIGINS` – comma-separated. Default local Vite origins only.

## Security notes

- Never commit `.env` or the SQLite file.
- Never log ingest or kiosk API keys.
- Never document or implement kiosk key delivery via the React bundle.
- `GET` and WebSocket are for trusted local use only until operator auth or a trusted proxy is added.
- Acknowledgements trust the client-supplied `acknowledged_by` / `kiosk_id` as audit metadata; cryptographic operator identity is **not** implemented yet.
- Do not expose Event Core directly on the public internet without a server-side acknowledgement proxy.
