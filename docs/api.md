# API – CableGuard Platform

Base URL (dev): `http://127.0.0.1:8000`

## Authentication

### Ingest (detectors / IO / MediaMTX health)

Header: `X-API-Key: <CABLEGUARD_INGEST_API_KEY>`

Required for:

- `POST /api/v1/events`
- `POST /api/v1/heartbeats`

Comparison uses constant-time `secrets.compare_digest`. Key values must never appear in logs or error responses.

### Kiosk (alarm acknowledgement)

Header: `X-Kiosk-Key: <CABLEGUARD_KIOSK_API_KEY>`

Required for:

- `POST /api/v1/events/{event_id}/acknowledge`

`acknowledged_by` and `kiosk_id` in the JSON body are **audit metadata only**. They are not cryptographically verified identities yet.

### Local read access

`GET` endpoints and `WS /ws/v1` currently require **no authentication**. They are intended for a trusted local kiosk on `127.0.0.1`. Before remote exposure, add operator authentication or a trusted reverse proxy.

## Endpoints

| Method | Path | Notes |
|---|---|---|
| GET | `/api/v1/health` | Liveness |
| GET | `/api/v1/status` | Services + open event count |
| GET | `/api/v1/status/history` | Status change history (`service_id`, limit, offset) |
| POST | `/api/v1/events` | Idempotent on `event_id` (see below) |
| GET | `/api/v1/events` | Filters: site_id, station_id, event_type, status, service_id; pagination limit/offset |
| GET | `/api/v1/events/{event_id}` | Detail |
| POST | `/api/v1/events/{event_id}/acknowledge` | Requires `X-Kiosk-Key`; see acknowledgement rules |
| POST | `/api/v1/heartbeats` | Upsert service_health; history only on status change |
| WS | `/ws/v1` | See `contracts/websocket-events.md` |

## Event ingest idempotence

Compared fields (server fields such as `received_at` are ignored):

`event_type`, `severity`, `site_id`, `station_id`, `camera_id`, `service_id`, `created_at`, `risk_score`, `status` (always `open` on ingest), `snapshot_url`, `clip_url`, `algorithm_version`, `model_sha256`, `config_sha256`, `payload_json`.

| Case | HTTP | WebSocket |
|---|---|---|
| New `event_id` | **201 Created** | `event.created` |
| Same `event_id`, identical payload | **200 OK** | none |
| Same `event_id`, different payload | **409 Conflict** | none |

## Media URL rules (`snapshot_url`, `clip_url`)

Allowed:

- `null`
- future API paths starting with `/api/`
- full `http://` or `https://` URLs

Rejected (**422**):

- Windows absolute paths (`C:\...`)
- `file://` URLs
- UNC paths (`\\server\share`)
- other absolute local filesystem paths

## Acknowledgement idempotence

| Case | HTTP | WebSocket |
|---|---|---|
| First acknowledgement | **200 OK** | `event.acknowledged` |
| Identical repeat (`acknowledged_by`, `kiosk_id`, `note`) | **200 OK** | none |
| Different details for same event | **409 Conflict** | none |
| Unknown event | **404 Not Found** | none |

Database enforces at most one acknowledgement row per `event_id`.

## Timestamps

All REST and WebSocket timestamps are timezone-aware UTC ISO 8601, for example:

`2026-07-20T20:20:45.873651+00:00`

Ingest rule: naive datetimes in request bodies are interpreted as UTC.

## Event types (examples)

- `fall_risk_detected`
- `safety_bar_alarm`
- `camera_offline`
- `io_fault`
