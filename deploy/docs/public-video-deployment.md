# Public HTTPS WHEP deployment – CableGuard Zahrádky horní stanice

This document covers Phase 1 audit results and the recommended architecture to expose
`zahradky-horni-stanice` to the **published Lovable frontend** over HTTPS/WHEP.

**Do not commit RTSP credentials, kiosk keys, or MediaMTX read passwords.**

---

## Phase 1 – Connectivity audit (2026-07-22, read-only)

| Item | Finding |
|------|---------|
| Public IPv4 (egress) | `194.12.35.108` (Czechia, AS49985 IP4ISP) |
| Reverse DNS | `nat35-108.hdinternet.cz` (**NAT** in hostname – likely ISP-level NAT) |
| PC LAN address | `10.6.1.40` (Ethernet, DHCP) |
| Default gateway | `10.6.1.1` |
| Traceroute hop 2 | `172.19.108.1` (private – ISP/CPE internal) |
| CGNAT on PC interface | No `100.64.0.0/10` address on PC |
| Router admin | **Unknown – must be confirmed** (gateway `10.6.1.1`) |
| Port 443 / 80 | Not listening on PC (no reverse proxy yet) |
| MediaMTX WHEP | `:8889` (all interfaces), API `:9997` **localhost only** ✓ |
| MediaMTX ICE UDP | `:8189` listening |
| Windows Firewall | Inbound allow rules for `mediamtx.exe` present |
| Caddy / Nginx | **Not installed** on MediaMTX PC |
| TLS today | None on video path |
| Custom domain in repo | **Not configured** – must be supplied |
| DNS provider | **Unknown** – confirm (Cloudflare recommended) |
| Lovable published origin | `https://98d9308a-3156-47c0-9d35-204618f8df3b.lovable.app` (from project metadata) |

### NAT / CGNAT assessment

The PC sits behind **private LAN `10.6.0.0/16`** with gateway `10.6.1.1`. Egress shows public
`194.12.35.108`, but the ISP hostname contains **`nat`** and traceroute crosses **`172.19.108.1`**.

**Action required before port forwarding:**

1. Log into router at `http://10.6.1.1` (or ISP CPE admin).
2. Compare **WAN IP** shown in router UI with `194.12.35.108`.
   - **If WAN IP = `194.12.35.108`** → port forwarding on `10.6.1.1` is viable.
   - **If WAN IP is private** (e.g. `100.x`, `10.x`, `172.16–31.x`) → **CGNAT**; inbound UDP 8189
     cannot be opened on the site router. Use **VPS + TURN** or **site-to-site VPN + VPS edge** (see below).

### Client access scope (decision needed)

| Mode | WHEP exposure | Auth model |
|------|-----------------|------------|
| **A – Corporate only** | VPN or IP allowlist on `video.<domain>` | Simplest firewall/Caddy ACL |
| **B – Internet + Lovable** | Public HTTPS WHEP + ICE UDP/TCP | Token auth required (not CORS alone) |

Published Lovable kiosk requires **mode B** for viewers outside LAN.

---

## Phase 2 – Recommended DNS layout

Replace `<DOMENA>` with your registered domain (e.g. `cableguard.cz`):

| Host | Purpose | Target |
|------|---------|--------|
| `monitor.<DOMENA>` | Future custom frontend (optional) | Lovable custom domain or static host |
| `video.<DOMENA>` | HTTPS WHEP + MediaMTX WebRTC handshake | `194.12.35.108` (A) or Caddy server |
| `api.<DOMENA>` | Event Core REST + WebSocket | Platform host (same PC or VPS) |

**WHEP URL (production frontend):**

```text
https://video.<DOMENA>/zahradky-horni-stanice/whep
```

**Event Core (production frontend):**

```text
https://api.<DOMENA>/api/v1
wss://api.<DOMENA>/ws/v1
```

**Lovable origin** (for CORS / `webrtcAllowOrigins` until custom domain):

```text
https://98d9308a-3156-47c0-9d35-204618f8df3b.lovable.app
```

No secrets in `VITE_*` variables.

---

## Phase 3 – Architecture options

### Option 1 – Direct expose (if WAN IP is public and port forward works)

```text
Internet → router (443/tcp, 8189/udp[+tcp]) → PC 10.6.1.40
         → Caddy TLS → 127.0.0.1:8889 (WHEP)
         → MediaMTX ICE UDP 8189
LAN      → http://10.6.1.40:8889 (unchanged)
```

### Option 2 – CGNAT / blocked UDP (ISP NAT)

```text
Camera RTSP → MediaMTX (site LAN)
                 ↓ VPN / RTSP tunnel
VPS (public IP) → MediaMTX or WHEP edge + coturn (TURN)
                 ↓ HTTPS WHEP
Lovable frontend
```

**Do not blindly open ports** if Phase 1 WAN check fails.

---

## Phase 4 – Port forwarding (Option 1 only)

Forward on **edge router** to `10.6.1.40`:

| Protocol | External | Internal | Service |
|----------|----------|----------|---------|
| TCP | 443 | 443 | Caddy (HTTPS WHEP) |
| UDP | 8189 | 8189 | WebRTC ICE |
| TCP | 8189 | 8189 | WebRTC ICE fallback (optional) |

**Do not forward:** 9997 (MediaMTX API), 8554 (RTSP), 8888 (HLS) unless explicitly required.

---

## Phase 5 – Firewall (Windows on MediaMTX PC)

Allow inbound:

- `Caddy` or `caddy.exe` – TCP 443
- `mediamtx.exe` – UDP 8189, TCP 8189 (if TCP ICE enabled)

Keep Event Core bound to `127.0.0.1:8000` behind Caddy on `api.<DOMENA>` when exposed.

---

## Phase 6 – Reverse proxy (Caddy)

See `deploy/caddy/Caddyfile.example`:

- TLS via Let's Encrypt (needs public DNS → your IP)
- Proxy to `127.0.0.1:8889`
- Preserve `Location` header for WHEP sessions
- Pass `OPTIONS`, `POST`, `PATCH`, `DELETE`
- No logging of query strings containing tokens

---

## Phase 7 – MediaMTX public profile

Copy `deploy/mediamtx/mediamtx.public.example.yml` → `deploy/mediamtx/mediamtx.public.yml` (gitignored).

Key settings:

- `webrtcAddress: 127.0.0.1:8889` (public only via Caddy; LAN can use separate local profile or keep `mediamtx.local.yml` for dev)
- `webrtcAdditionalHosts: [video.<DOMENA>]`
- `webrtcAllowOrigins` – Lovable origin + `https://monitor.<DOMENA>` only
- `apiAddress: 127.0.0.1:9997` (never expose)
- RTSP source only in gitignored file

For **dual LAN + public**, run two configs or one config with LAN-friendly bind – see setup script.

---

## Phase 8 – Stream authentication (recommended POC)

**CORS is not security.** Do not leave WHEP anonymously public.

| Option | Complexity | Fits Lovable |
|--------|------------|--------------|
| A. MediaMTX JWT | High | Yes (Authorization header) |
| B. Platform token endpoint + MediaMTX HTTP auth | Medium | **Recommended** |
| C. Caddy basic auth | Low | **No** (secrets in browser) |
| D. VPN only | Low | **No** (Lovable viewers not on VPN) |

**Recommended POC:** **B – Platform HTTP auth hook**

1. MediaMTX `authMethod: http` → `http://127.0.0.1:8000/internal/mediamtx/auth`
2. Platform issues short-lived **viewer token** (server-side only secret to mint tokens)
3. Monitor calls `GET /api/v1/streams/zahradky-horni-stanice/whep-access` → `{ "authorization": "Bearer …" }`
4. `whepClient` sends `Authorization` on POST/PATCH/DELETE

Implement auth endpoint on platform before exposing `video.<DOMENA>` to the internet.

---

## Phase 9 – Production frontend (cableguard-monitor)

Tracked file: `.env.production` (public URLs only, no secrets):

```env
VITE_VIDEO_MODE=whep
VITE_WHEP_BASE_URL=https://video.<DOMENA>
VITE_API_BASE_URL=https://api.<DOMENA>
VITE_WS_URL=wss://api.<DOMENA>/ws/v1
VITE_USE_MOCKS=false
```

Local dev remains `.env.local` with `localhost` URLs.

---

## Phase 10 – Acceptance tests

Perform **from mobile data** (not LAN Wi‑Fi):

1. Open published Lovable kiosk URL
2. DevTools → Network: `OPTIONS` + `POST 201` + `PATCH 204` on `https://video.<DOMENA>/…/whep`
3. `chrome://webrtc-internals` → ICE **connected**, `framesReceived` increasing
4. Restart MediaMTX → OFFLINE → automatic reconnect

---

## Phase 11 – Lovable rollout (after DNS + TLS + auth live)

1. Sync Lovable project from GitHub `main` (after monitor PR merge)
2. Confirm Lovable build env matches `.env.production` **or** relies on committed `.env.production`
3. **Publish → Update**
4. Open published kiosk → verify live video from mobile data
5. Confirm no RTSP/kiosk secrets in browser bundle (`npm run verify:secrets`)

---

## Security risks

| Risk | Mitigation |
|------|------------|
| Anonymous WHEP | HTTP auth + short-lived tokens |
| RTSP in git | gitignored `mediamtx.public.yml` only |
| Exposed API :9997 | bind 127.0.0.1 only |
| CGNAT / no ICE | VPS TURN or verify port forward |
| Token in URL logs | Prefer Authorization header; strip query logs |

---

## Open inputs required from operator

1. Registered **`<DOMENA>`** and DNS provider (Cloudflare?)
2. Router admin access and **WAN IP** screenshot
3. Corporate-only vs **internet** viewers
4. Whether **UDP 8189** inbound test from mobile succeeds after forward
