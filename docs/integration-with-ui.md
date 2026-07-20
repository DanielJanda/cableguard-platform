# Integration with UI (Lovable / React)

## What the UI should use

| Need | Source |
|---|---|
| Alarm list / detail | `GET /api/v1/events` |
| Live alarm tile (immediate) | `WS /ws/v1` → `event.created` (full safe EventRead fields, no `payload_json`) |
| Ack button | `POST /api/v1/events/{id}/acknowledge` with header `X-Kiosk-Key` |
| Service tiles | `system.snapshot` + `service.updated` / `service.offline` |
| Live video | MediaMTX WHEP `{WHEP_BASE_URL}/{stream}/whep` – **not** Event Core |

## Ack request

Headers:

```http
X-Kiosk-Key: <CABLEGUARD_KIOSK_API_KEY>
Content-Type: application/json
```

Body:

```json
{
  "acknowledged_by": "operator-name",
  "kiosk_id": "kiosk-zahradky-1",
  "note": "optional"
}
```

`acknowledged_by` and `kiosk_id` are audit labels supplied by the kiosk app. They are **not** cryptographically verified user identities yet.

Identical repeat requests are idempotent (HTTP 200, no duplicate WebSocket message).

## `event.created` data (WebSocket)

Contains all fields needed for the alarm screen without a follow-up GET:

`event_id`, `event_type`, `severity`, `site_id`, `station_id`, `camera_id`, `service_id`, `created_at`, `received_at`, `risk_score`, `status`, `snapshot_url`, `clip_url`, `algorithm_version`, `model_sha256`, `config_sha256`.

`payload_json` is omitted from WebSocket messages.

## Media URLs

Use `null`, `/api/...`, or public `http(s)://` URLs only. Never send Windows/local filesystem paths to Event Core.

## Local security model

- Backend listens on `127.0.0.1` in development.
- Ingest uses `X-API-Key`; acknowledgement uses `X-Kiosk-Key`.
- `GET` and WebSocket are currently **unauthenticated** and intended only for a trusted local kiosk process.
- Before remote/internet exposure: add operator authentication or place Event Core behind a trusted reverse proxy.

## Do not

- Open SQLite from the browser
- Connect to RTSP from the browser (use WHEP)
- Drive USB / semaphore from the UI or Event Core
- Embed ingest or kiosk API keys in a public frontend build
