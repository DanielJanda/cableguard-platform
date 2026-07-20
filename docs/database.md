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

One row per acknowledged event (`UNIQUE event_id`, FK → `events.event_id`). Audit fields: `acknowledgement_id`, `acknowledged_by`, `kiosk_id`, `note`, timestamp.

## Migrations

```powershell
cd <PROJECT_ROOT>
.\.venv\Scripts\Activate.ps1
$env:PYTHONPATH = "<PROJECT_ROOT>\backend"
cd backend
alembic upgrade head
```

Revisions:

- `0001_initial` – core tables
- `0002_ack_event_fk` – unique `acknowledgements.event_id` + foreign key

Or: `.\scripts\reset_development_database.ps1`

## Timestamps

All `DateTime(timezone=True)` columns store UTC. API output uses ISO 8601 with explicit offset (`+00:00`).
