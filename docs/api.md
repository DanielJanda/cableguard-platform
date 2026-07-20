# API – CableGuard Platform

Base URL (dev): `http://127.0.0.1:8000`

## Ingest auth

Header: `X-API-Key: <CABLEGUARD_INGEST_API_KEY>`

Required for:

- `POST /api/v1/events`
- `POST /api/v1/heartbeats`

Not required for read endpoints or acknowledgements (operator auth planned later).

## Endpoints

| Method | Path | Notes |
|---|---|---|
| GET | `/api/v1/health` | Liveness |
| GET | `/api/v1/status` | Services + open event count |
| GET | `/api/v1/status/history` | Status change history (`service_id`, limit, offset) |
| POST | `/api/v1/events` | Idempotent on `event_id` (201 create / 200 existing) |
| GET | `/api/v1/events` | Filters: site_id, station_id, event_type, status, service_id; pagination limit/offset |
| GET | `/api/v1/events/{event_id}` | Detail |
| POST | `/api/v1/events/{event_id}/acknowledge` | Body: acknowledged_by, kiosk_id, note? |
| POST | `/api/v1/heartbeats` | Upsert service_health; history only on status change |
| WS | `/ws/v1` | See `contracts/websocket-events.md` |

## Event types (examples)

- `fall_risk_detected`
- `safety_bar_alarm`
- `camera_offline`
- `io_fault`
