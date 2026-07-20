# Integration with UI (Lovable / React)

## What the UI should use

| Need | Source |
|---|---|
| Alarm list / detail | `GET /api/v1/events` |
| Live alarms | `WS /ws/v1` → `event.created` |
| Ack button | `POST /api/v1/events/{id}/acknowledge` |
| Service tiles | `system.snapshot` + `service.updated` / `service.offline` |
| Live video | MediaMTX WHEP `{WHEP_BASE_URL}/{stream}/whep` – **not** Event Core |

## Ack payload

```json
{
  "acknowledged_by": "operator-name",
  "kiosk_id": "kiosk-zahradky-1",
  "note": "optional"
}
```

## Do not

- Open SQLite from the browser
- Connect to RTSP from the browser (use WHEP)
- Drive USB / semaphore from the UI
- Embed ingest API keys in a public frontend build (ingest is for services; UI reads/acks with future operator auth)
