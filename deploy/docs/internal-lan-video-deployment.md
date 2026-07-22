# Internal LAN WHEP – CableGuard Zahrádky (company network only)

Published **Lovable** frontend (HTTPS) on a corporate PC connects **directly** from the
browser to internal MediaMTX. Lovable does **not** proxy video.

```text
Browser (HTTPS, lovable.app) ──WHEP──► MediaMTX 10.6.1.40:8889 ──RTSP──► camera
```

No public IPv4, port forwarding, VPS, or TURN required.

---

## 1. Internal availability checklist

| Check | Command / URL |
|-------|----------------|
| Embedded player | `http://10.6.1.40:8889/zahradky-horni-stanice/` |
| WHEP OPTIONS | `curl -X OPTIONS http://10.6.1.40:8889/zahradky-horni-stanice/whep` |
| Path ready | `http://127.0.0.1:9997/v3/paths/get/zahradky-horni-stanice` → `ready: true` |
| ICE UDP | UDP `:8189` listening on MediaMTX PC |

---

## 2. MediaMTX internal profile

Copy `deploy/mediamtx/mediamtx.internal-lan.example.yml` → `mediamtx.internal-lan.yml` (gitignored).

- Keep `webrtcAddress: :8889` for LAN HTTP (dev/fallback)
- Restrict `webrtcAllowOrigins` to Lovable + localhost + optional internal HTTPS host
- Set `webrtcAdditionalHosts: [10.6.1.40, video-internal.<DOMENA>]`
- API stays `127.0.0.1:9997` only

---

## 3. Local Network Access (HTTPS → HTTP)

Chrome/Edge may block `https://*.lovable.app` → `http://10.6.1.40:8889` until the user
allows **local network access**.

If blocked after permission prompt:

- Use **internal HTTPS** (recommended operational state):

```text
https://video-internal.<DOMENA>  →  Caddy TLS  →  127.0.0.1:8889
```

Internal DNS (split-horizon, **no public A record**):

```text
video-internal.<DOMENA>  A  10.6.1.40
```

Certificate options:

| Option | When to use |
|--------|-------------|
| **A. DNS challenge** | Corporate domain on internal/public DNS you control |
| **B. Internal CA** | AD CS / corporate PKI – install trust on kiosks |
| **C. Caddy local CA** | PoC – install Caddy root on each kiosk browser |

See `deploy/caddy/Caddyfile.internal.example`.

Keep HTTP `:8889` until HTTPS WHEP test passes.

---

## 4. CORS origins (not authentication)

```yaml
webrtcAllowOrigins:
  - https://98d9308a-3156-47c0-9d35-204618f8df3b.lovable.app
  - http://localhost:8080
  - http://127.0.0.1:8080
  - https://video-internal.example.com
```

Camera is not routed to the internet; LAN/VPN reachability is the boundary.

---

## 5. Monitor / Lovable configuration

Build with internal mode (see `cableguard-monitor`):

```env
VITE_DEPLOYMENT_MODE=internal-lan
VITE_VIDEO_MODE=whep
VITE_WHEP_BASE_URL=http://10.6.1.40:8889
```

Or after internal HTTPS:

```env
VITE_WHEP_BASE_URL=https://video-internal.<DOMENA>
```

Default production build (`VITE_DEPLOYMENT_MODE` unset) → **placeholder**.

---

## 6. Acceptance (corporate PC)

1. Open published Lovable kiosk
2. Allow local network access if prompted
3. DevTools: WHEP POST **201**, PATCH **204**
4. ICE **connected**, video **1280×720**, framesReceived increasing
5. MediaMTX restart → OFFLINE → auto reconnect → LIVE

Outside corporate network: expect **OFFLINE** (no crash).
