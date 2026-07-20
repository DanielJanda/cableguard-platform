# Database – SQLite

File: `data/cableguard.sqlite3` (gitignored)  
Journal: WAL

## Tables

### events

Unique `event_id`. Fields include type, severity, site/station/camera/service, timestamps, risk_score, status (`open`|`acknowledged`|`closed`), snapshot/clip URLs, algorithm/model/config hashes, `payload_json`.

### service_health

PK `service_id`. Current status (`healthy`|`degraded`|`offline`), last heartbeat, optional flags, `details_json`.

### service_status_history

Append-only status transitions (not every heartbeat).

### acknowledgements

Audit row per ack: `acknowledgement_id`, `event_id`, `acknowledged_by`, `kiosk_id`, `note`, timestamp.

## Migrations

```powershell
cd backend
$env:PYTHONPATH = (Get-Location).Path
alembic upgrade head
```

Or: `.\scripts\reset_development_database.ps1`
