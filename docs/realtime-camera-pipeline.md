# Realtime camera pipeline

## Registry

Committed safe metadata lives in `deploy/cameras/registry.yml`.

Each camera entry contains:

- `camera_id`, `site_id`, `station_id`
- `stream_name` (MediaMTX path)
- `rtsp_proxy_path` (local proxy only, no credentials)
- `whep_path` (relative WHEP endpoint)
- `enabled`

Real RTSP camera sources remain in gitignored `deploy/mediamtx/mediamtx.local.yml`.

## API

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/cameras` | List registry entries |
| `GET /api/v1/cameras/{camera_id}` | Single camera metadata |
| `GET /api/v1/streams/{stream_name}/status` | MediaMTX path readiness |

Stream status values:

- `mediamtx_offline` – Control API unreachable
- `path_not_found` – path missing in MediaMTX
- `source_not_ready` – path exists, source not ready
- `ready` – path ready, no readers
- `streaming` – path ready with active readers

## Local endpoints

| Service | URL |
|---|---|
| RTSP proxy | `rtsp://127.0.0.1:8554/zahradky-horni-stanice` |
| WHEP | `http://127.0.0.1:8889/zahradky-horni-stanice/whep` |
| Control API | `http://127.0.0.1:9997` |

Never expose RTSP credentials via Event Core or the camera registry.
