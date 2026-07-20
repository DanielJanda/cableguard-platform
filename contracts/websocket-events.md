# WebSocket contract – CableGuard Platform

Endpoint: `WS /ws/v1`

Authentication: none (local trusted kiosk only for now).

## Envelope

All messages use:

```json
{
  "type": "event.created",
  "data": {},
  "sent_at": "2026-07-20T12:00:00+00:00"
}
```

`sent_at` is timezone-aware UTC ISO 8601.

## Message types

| type | When |
|---|---|
| `system.snapshot` | Immediately after connect |
| `event.created` | New event inserted (not idempotent replay) |
| `event.acknowledged` | First acknowledgement only (not idempotent replay) |
| `service.updated` | Heartbeat upsert |
| `service.offline` | Heartbeat timeout marked offline |

Clients should reconnect and treat `system.snapshot` as full replace of the service list. Snapshot does **not** include unbounded history.

## `system.snapshot` data

```json
{
  "services": [ /* ServiceHealthRead[] */ ],
  "open_events": 0,
  "heartbeat_timeout_sec": 6.0
}
```

## `event.created` data

Full safe alarm fields for immediate UI rendering (no `payload_json`):

```json
{
  "event_id": "11111111-1111-4111-8111-111111111111",
  "event_type": "fall_risk_detected",
  "severity": "alarm",
  "site_id": "zahradky",
  "station_id": "horni_stanice",
  "camera_id": "kamera4",
  "service_id": "zahradky-horni-pad-detector",
  "created_at": "2026-07-20T12:00:00+00:00",
  "received_at": "2026-07-20T12:00:00.123456+00:00",
  "risk_score": 0.75,
  "status": "open",
  "snapshot_url": null,
  "clip_url": null,
  "algorithm_version": "zahradky-fall-v1",
  "model_sha256": "29B17EAF",
  "config_sha256": "deadbeef"
}
```

## `event.acknowledged` data

```json
{
  "event_id": "11111111-1111-4111-8111-111111111111",
  "acknowledgement_id": "…",
  "acknowledged_by": "operator-name",
  "kiosk_id": "kiosk-zahradky-1",
  "acknowledged_at": "2026-07-20T12:00:05+00:00",
  "status": "acknowledged"
}
```

## `service.updated` / `service.offline` data

```json
{
  "service_id": "zahradky-horni-pad-detector",
  "site_id": "zahradky",
  "station_id": "horni_stanice",
  "service_type": "fall_detector",
  "status": "healthy",
  "last_heartbeat_at": "2026-07-20T12:00:00+00:00"
}
```
