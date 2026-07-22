# MediaMTX – CableGuard local video POC

Managed local MediaMTX for **Zahrádky horní stanice** WebRTC preview.

## Architecture

```text
horní kamera (RTSP)
  → MediaMTX path: zahradky-horni-stanice
  → WebRTC / WHEP (browser)
  → cableguard-monitor (iframe POC, later WHEP VideoPlayer)
```

Event Core (`cableguard-platform` backend) is **not** part of the video path.

## Setup (once)

1. Download Windows `mediamtx.exe` into `runtime/mediamtx/` (gitignored).
2. Copy `deploy/mediamtx/mediamtx.example.yml` → `deploy/mediamtx/mediamtx.local.yml`.
3. Fill `mediamtx.local.yml` with real RTSP source locally — **never commit**.
4. Or run `scripts/setup_mediamtx_local_config.ps1` to seed `zahradky-horni-stanice` from an existing local legacy config (credentials stay local).

## Scripts

| Script | Purpose |
|---|---|
| `scripts/start_mediamtx.ps1` | Start managed instance |
| `scripts/stop_mediamtx.ps1` | Stop managed instance only |
| `scripts/status_mediamtx.ps1` | Health + path readiness |
| `scripts/test_mediamtx_stream.ps1` | HTTP checks without printing RTSP |
| `scripts/setup_mediamtx_local_config.ps1` | Create local yml from legacy path (optional) |

## Safe browser URLs (default ports)

| Endpoint | URL |
|---|---|
| Embedded player | `http://127.0.0.1:8889/zahradky-horni-stanice` |
| WHEP | `http://127.0.0.1:8889/zahradky-horni-stanice/whep` |

## Security

- RTSP credentials live only in `mediamtx.local.yml` / `mediamtx.public.yml` (gitignored).
- Frontend never receives RTSP URL or camera password.
- Do not expose `mediamtx.local.yml`, `mediamtx.public.yml`, or legacy configs with credentials.

## Public HTTPS / internet WHEP

See **`deploy/docs/public-video-deployment.md`** for:

- WAN/NAT audit checklist
- DNS layout (`video.<domain>`, `api.<domain>`)
- Caddy TLS reverse proxy example
- `mediamtx.public.example.yml` (ICE, CORS, `webrtcAdditionalHosts`)
- Port forwarding UDP/TCP 8189
- Stream authentication model
- Lovable publish steps

Example configs:

| File | Purpose |
|---|---|
| `deploy/mediamtx/mediamtx.public.example.yml` | Internet WebRTC profile template |
| `deploy/caddy/Caddyfile.example` | HTTPS WHEP proxy |
| `deploy/public/domains.example.env` | Domain/IP placeholders |

## Legacy note

An older Docker-based MediaMTX install may exist at `C:\Users\mega\Documents\mediamtx\` with path name `kamera4`. This managed POC uses **`zahradky-horni-stanice`** and a separate process tracked by `runtime/mediamtx/mediamtx.pid`.
