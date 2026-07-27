# SECURITY — secrets a bezpečnostní model

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

---

## Zásada: secrets pouze env / gitignored config

| Secret | Kde žije | Nikdy |
|---|---|---|
| `CABLEGUARD_INGEST_API_KEY` (ingest key) | platform `.env` (gitignored); posílá detektor/simulátor v `X-API-Key` | v Gitu, ve frontendu |
| `CABLEGUARD_KIOSK_API_KEY` (kiosk key) | monitor `.env.internal-lan.local` (gitignored), čte **jen** Vite server-side (bez `VITE_` prefixu); injektuje BFF do `X-Kiosk-Key` | v browser bundle, v Gitu |
| RTSP credentials kamer | `deploy/mediamtx/mediamtx.local.yml` (gitignored); detector `.env` (gitignored) | ve frontendu, v Gitu, v log výstupech skriptů |
| Telegram token/chat | detector `.env` (gitignored); Admin Studio → Windows Credential Manager (`CableGuard.Telegram.*`) | v Gitu, v `notifications.json`, v logách |
| RTSP camera passwords (Admin Studio) | Windows Credential Manager (`CableGuard.Camera.*`); nikdy `cameras.json` | v Gitu, ve frontendu, v logách |

Vynucení kódem, ne jen konvencí:

- **Event Core odmítne placeholder secrets** (503) — `core/secrets.py`, testováno `test_secrets.py`. Bez nakonfigurovaného klíče se ingest/ack prostě nespustí.
- **Build scan:** `npm run verify:secrets` po produkčním buildu skenuje `.output` na `CABLEGUARD_KIOSK_API_KEY`, `CABLEGUARD_INGEST_API_KEY`, `X-Kiosk-Key`, `X-API-Key`, `rtsp://` — PASS 2026-07-27.
- **BFF 503**, pokud kiosk key chybí — klíč se nedá „zapomenout“ tiše.
- Setup skripty (`setup_mediamtx_local_config.ps1`, `setup_second_zahradky_camera.ps1`) přenášejí RTSP credentials mezi gitignored soubory, aniž by je vypisovaly.

## Browser secret restrictions

- Prohlížeč nikdy nedrží API klíče: acknowledge jde přes BFF (`/bff/events/{id}/acknowledge`), který klíč doplní server-side.
- Video: prohlížeč zná jen WHEP URL (`http://10.6.1.40:8889/...`) — žádné RTSP credentials; MediaMTX je jediný, kdo se ke kameře hlásí.
- `verify-api-contract.test.mjs` staticky hlídá, že `apiClient` neobsahuje `X-Kiosk-Key`.

## MediaMTX source config

- `mediamtx.example.yml` (committed) obsahuje pouze placeholdery; reálné zdroje jen v `mediamtx.local.yml` (gitignored).
- `webrtcAllowOrigin: http://10.6.1.40:8080` — WHEP handshake omezen na origin monitoru.
- API `:9997` bind na `127.0.0.1` — z LAN nedostupné.

## Runtime gitignore

Gitignored ve všech repech: `.env*` (kromě `*.example`), `runtime/` (MediaMTX binárka, PID, logy, JSONL eventy), `data/*.sqlite3*`, modely mimo LFS, videa/logy detektoru.

## DB zálohy

**Aktuálně žádné automatické zálohy neexistují.** SQLite soubor `data/cableguard.sqlite3` (WAL) žije jen na disku `10.6.1.40`. Bezpečný vývojový reset: `scripts/reset_development_database.ps1 -ConfirmReset`. Zálohy = Phase 9 (viz RISKS_AND_TECH_DEBT.md).

## Trusted-LAN assumption

Současný model **vědomě** předpokládá důvěryhodnou firemní LAN:

- `GET /api/v1/*` a `WS /ws/v1` jsou **bez autentizace** — kdokoli v LAN může číst eventy a stav služeb.
- Zápisové operace klíče mají (ingest, kiosk/ack), čtení ne.
- HTTP bez TLS uvnitř LAN.
- Firewall otevírá porty jen pro Domain/Private profily; Public profil zůstává zavřený.

Mitigace hranice: přístup do firemní LAN je řízen mimo CableGuard (síťová infrastruktura firmy).

## Explicitní omezení

**Současný internal-LAN runtime není navržen jako veřejná internetová služba.** Vystavení do internetu bez Phase 9 (auth GET/WS, TLS, reverse proxy, rate limiting) je zakázáno. Public HTTPS experimenty jsou DEFERRED (platform PR #6, monitor PR #5) a nemergují se.
