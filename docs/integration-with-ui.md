# Integration with UI (Lovable / React)

## What the UI should use

| Need | Source |
|---|---|
| Alarm list / detail | `GET /api/v1/events` |
| Live alarm tile (immediate) | `WS /ws/v1` → `event.created` (full safe EventRead fields, no `payload_json`) |
| Ack button | **Server-side proxy / BFF** → `POST /api/v1/events/{id}/acknowledge` (see below) |
| Service tiles | `system.snapshot` + `service.updated` / `service.offline` |
| Live video | MediaMTX WHEP `{WHEP_BASE_URL}/{stream}/whep` – **not** Event Core |

## Acknowledgement architecture (required)

The React/Vite frontend in `cableguard-monitor` **must never** hold or send `X-Kiosk-Key` directly.

| Layer | Responsibility |
|---|---|
| Browser (React) | Calls a same-origin proxy route, e.g. `/bff/events/{id}/acknowledge`, with JSON body only |
| Server-side proxy / BFF | Reads `CABLEGUARD_KIOSK_API_KEY` from server config and adds `X-Kiosk-Key` when forwarding to Event Core |
| Event Core | Validates `X-Kiosk-Key` and processes acknowledgement |

### Rules for the frontend

1. The React app **never** stores `CABLEGUARD_KIOSK_API_KEY`.
2. The key **must not** use a `VITE_` prefix (Vite exposes `VITE_*` to the client bundle).
3. The key **must not** appear in JavaScript source, `localStorage`, `sessionStorage`, or any browser-visible config.
4. A published static build (e.g. on lovable.app) **cannot** safely hold the kiosk key — acknowledgement must go through a trusted server-side hop.
5. Even if the key were embedded, it would be visible in DevTools network headers; treat browser-held secrets as public.

### Browser request (what React sends)

```http
POST /bff/events/{event_id}/acknowledge
Content-Type: application/json
```

```json
{
  "acknowledged_by": "operator-name",
  "kiosk_id": "kiosk-zahradky-1",
  "note": "optional"
}
```

No `X-Kiosk-Key` header from the browser.

### Server-side forward (what the proxy adds)

```http
POST http://127.0.0.1:8000/api/v1/events/{event_id}/acknowledge
X-Kiosk-Key: <CABLEGUARD_KIOSK_API_KEY>
Content-Type: application/json
```

`CABLEGUARD_KIOSK_API_KEY` lives in Event Core `.env` and/or the proxy's server environment — **not** in the frontend repo as a `VITE_*` variable.

`acknowledged_by` and `kiosk_id` remain audit labels in the JSON body. They are **not** cryptographically verified user identities yet.

Identical repeat requests are idempotent (HTTP 200, no duplicate WebSocket message).

## Local development: Vite dev proxy (in `cableguard-monitor`)

The Vite proxy is configured in **`cableguard-monitor`**, not in `cableguard-platform`.

Pattern:

1. Start Event Core on `127.0.0.1:8000` with `CABLEGUARD_KIOSK_API_KEY` in platform `.env`.
2. In `cableguard-monitor`, load `CABLEGUARD_KIOSK_API_KEY` as a **Node/server** env var (shell, `.env.local` without `VITE_`, or `vite.config.ts` reading `process.env`).
3. Configure Vite `server.proxy` so the browser calls a same-origin path; the proxy forwards to Event Core and injects `X-Kiosk-Key`.

Example shape (to be implemented in `cableguard-monitor`):

```typescript
// vite.config.ts — server-side only, not shipped to the browser bundle
export default defineConfig({
  server: {
    proxy: {
      "/bff": {
        target: "http://127.0.0.1:8000",
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/bff/, "/api/v1"),
        configure: (proxy) => {
          proxy.on("proxyReq", (proxyReq) => {
            const key = process.env.CABLEGUARD_KIOSK_API_KEY;
            if (key) proxyReq.setHeader("X-Kiosk-Key", key);
          });
        },
      },
    },
  },
});
```

React then calls `fetch("/bff/events/{id}/acknowledge", { method: "POST", ... })` with no secret headers.

## Production / kiosk deployment

The same pattern applies with **Caddy**, **Nginx**, or a small local **BFF** service:

- Browser → same-origin `/bff/...` or equivalent
- Edge/proxy → Event Core with `X-Kiosk-Key` added server-side

Do not expose Event Core directly to the public internet without this layer.

## `event.created` data (WebSocket)

Contains all fields needed for the alarm screen without a follow-up GET:

`event_id`, `event_type`, `severity`, `site_id`, `station_id`, `camera_id`, `service_id`, `created_at`, `received_at`, `risk_score`, `status`, `snapshot_url`, `clip_url`, `algorithm_version`, `model_sha256`, `config_sha256`.

`payload_json` is omitted from WebSocket messages.

## Media URLs

Use `null`, `/api/...`, or public `http(s)://` URLs only. Never send Windows/local filesystem paths to Event Core.

## Local security model

- Event Core listens on `127.0.0.1` in development.
- Ingest uses `X-API-Key` (detectors / simulator only — also never in browser).
- Acknowledgement uses `X-Kiosk-Key` on the **server-side hop** to Event Core, not in React.
- `GET` and WebSocket are currently **unauthenticated** and intended only for a trusted local kiosk on the same machine/LAN.
- Before remote/internet exposure: keep the kiosk key on the proxy/BFF; add operator authentication at the edge if needed.

## Do not

- Put `CABLEGUARD_KIOSK_API_KEY` or `VITE_*` kiosk secrets in the React app
- Send `X-Kiosk-Key` from browser JavaScript
- Open SQLite from the browser
- Connect to RTSP from the browser (use WHEP)
- Drive USB / semaphore from the UI or Event Core
- Embed ingest API keys in a public frontend build
