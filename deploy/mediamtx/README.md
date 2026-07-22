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

## Public HTTPS / internet WHEP

**Deferred** – see draft PR #6. Current architecture is **internal LAN only**.

## Internal LAN WHEP (current)

See **`deploy/docs/internal-lan-video-deployment.md`** for:

- LAN availability (`10.6.1.40:8889`)
- Lovable `internal-lan` deployment mode
- MediaMTX CORS for `*.lovable.app` + localhost
- Internal HTTPS via Caddy (`video-internal.<DOMENA>` → `127.0.0.1:8889`)
- Local Network Access / mixed-content notes

Example configs:

| File | Purpose |
|---|---|
| `deploy/mediamtx/mediamtx.internal-lan.example.yml` | LAN WebRTC profile |
| `deploy/caddy/Caddyfile.internal.example` | Internal HTTPS WHEP |

## Legacy note

An older Docker-based MediaMTX install may exist at `C:\Users\mega\Documents\mediamtx\` with path name `kamera4`. This managed POC uses **`zahradky-horni-stanice`** and a separate process tracked by `runtime/mediamtx/mediamtx.pid`.
