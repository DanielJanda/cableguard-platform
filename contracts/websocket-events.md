# WebSocket contract – CableGuard Platform

Endpoint: `WS /ws/v1`

## Envelope

```json
{
  "type": "event.created",
  "data": {},
  "sent_at": "2026-07-20T12:00:00+00:00"
}
```

## Message types

| type | When |
|---|---|
| `system.snapshot` | Immediately after connect |
| `event.created` | New event inserted (not idempotent replay) |
| `event.acknowledged` | Event acknowledged |
| `service.updated` | Heartbeat upsert / status change |
| `service.offline` | Heartbeat timeout marked offline |

Clients should reconnect and treat `system.snapshot` as full replace of service list.
