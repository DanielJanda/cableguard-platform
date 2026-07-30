# Incident pipeline — office RC note (2026-07-30)

## Status

**Office-tested (automated inject):** fall event → PENDING media → MediaMTX playback extract → READY snapshot + READY clip (~45 s = 15 pre + 30 post) → HTTP Range seek via Event Core BFF.

Event example: `90db0980-ece9-4fdb-b622-b2cfc36a3040`

| Check | Result |
|---|---|
| Alarm fields on ingest | `camera_id=office-63`, statuses PENDING |
| Clip duration | 44.989 s (requested 45.0) |
| Codec | H.264 1280×720 |
| Snapshot JPEG | HTTP 200 |
| Clip Range | HTTP 206 |
| Path traversal | rejected (automated test) |
| Golden master / fall algo | unchanged |

## Branches

| Repo | Branch | Depends on |
|---|---|---|
| platform | `feat/incident-pipeline-release` | PR #35 (layout API) |
| monitor | `feat/operator-events-release` | PR #13 |
| detector | `feat/fall-event-publisher-release` | PyAV tip `47366e2` |

### Merge order

1. Platform PR #32 → #33 → #34 (if not merged) then **#35**, then **incident-pipeline-release**
2. Monitor **#13**, then **operator-events-release**
3. Detector **fall-event-publisher-release** (camera_id `office-63` + episode payload only)

## Not done in this PR set (deferred / P1)

- Full Control Center Overview redesign + START ALL readiness matrix
- 30 min soak report (process left running; formal metrics TBD)
- Full retention worker dry-run UI
- Zahrádky on-site commissioning
- Continuous alarm siren until ACK (kept 3 s notification per office preference)

## Security

- MediaMTX playback only via `127.0.0.1:9996`
- Monitor uses `/api/v1/events/{id}/snapshot|clip` only
- `runtime/incidents/` gitignored
- No RTSP credentials in browser or event payloads
