# CURRENT_STATE — skutečný stav CableGuard

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

Zdrojem pravdy je kód na `main` a reálné akceptační testy, ne starší dokumentace. Rozpory staré dokumentace vs. kód jsou zaznamenány na konci.

---

## Produkčně/integračně ověřené (CONFIRMED)

### Interní LAN runtime

Celý stack běží na jednom PC **`10.6.1.40`** a je dostupný z firemní LAN. Ověřeno **vizuálně z druhého firemního PC** (2026-07-22): dashboard, kiosk, živé video, alarm, acknowledge.

| Služba | Adresa | Stav |
|---|---|---|
| Event Core | `0.0.0.0:8000` | CONFIRMED |
| Monitor (Vite dev + BFF) | `0.0.0.0:8080` | CONFIRMED |
| MediaMTX WHEP | `:8889` (HTTP) | CONFIRMED |
| WebRTC ICE | `:8189` (UDP) | CONFIRMED |
| MediaMTX RTSP proxy | `:8554` (TCP, lokální použití) | CONFIRMED — využit detektorem v integračním testu, běžně bez LAN expozice |

Pozn.: runtime **není persistentní služba** — po restartu PC nebo ukončení procesů nic neběží automaticky (viz RISKS). Start: `scripts/start_internal_cableguard.ps1`.

### Kamera (produkční stream)

- MediaMTX path: **`zahradky-horni-stanice`**
- Zdroj: horní stanice Zahrádky, kamera **`.92`** (potvrzeno aktuální gitignored `mediamtx.local.yml`), H.264 substream
- Ověřené rozlišení v prohlížeči: **1280×720 @ ~20 FPS**
- RTSP credentials pouze v gitignored `deploy/mediamtx/mediamtx.local.yml` — nikdy v Gitu ani frontendu

### Monitor (main)

- **Routy:** `/dashboard`, `/events`, `/system`, `/kiosk/zahradky/horni-stanice`, `/kiosk/zahradky/dolni-stanice`, `/` (redirect)
- **Nativní WHEP player** (`src/services/whepClient.ts`): OPTIONS → POST (očekává 201) → trickle-ICE PATCH (204) → DELETE cleanup; exponenciální backoff 1s→30s
- **Reconnect:** automatický při selhání connectu, manuální tlačítko „Obnovit video“; StrictMode guard přes generation ref (žádné duplicitní WHEP sessions)
- **BFF acknowledge:** Vite dev middleware `POST /bff/events/{id}/acknowledge` → injektuje `X-Kiosk-Key` server-side (`CABLEGUARD_KIOSK_API_KEY` bez `VITE_` prefixu)
- **WebSocket:** konzumuje `system.snapshot`, `event.created`, `event.acknowledged`, `service.updated`, `service.offline`; reconnect backoff max 15 s
- **Mock/placeholder fallback:** `VITE_USE_MOCKS` (default true) — plně funkční UI bez backendu
- **Interní profil:** `VITE_DEPLOYMENT_MODE=internal-lan` (`.env.internal-lan.local`, gitignored), start `scripts/start_internal_monitor.ps1`

### Platform / Event Core (main)

- **FastAPI** + **SQLite (WAL)** + **Alembic** (migrace `0001`, `0002`)
- REST: `POST /api/v1/events` (X-API-Key), `POST /api/v1/events/{id}/acknowledge` (X-Kiosk-Key), `GET /events`, `GET /events/{id}`, `GET /status`, `GET /status/history`, `POST /heartbeats`, `GET /health`
- **Idempotence eventů:** unikátní `event_id`; identický duplikát → 200 bez WS; odlišný payload → 409
- **Idempotence acknowledge:** jeden ack na event; identický opakovaný → 200 (existující), odlišný → 409
- **Health/status:** heartbeat timeout 6 s, background monitor (1 s tick), přechody healthy↔offline s historií
- **WS eventy:** `system.snapshot` (při připojení), `event.created`, `event.acknowledged`, `service.updated`, `service.offline`
- Testy: **37 passed** (2026-07-27)

### Detector — golden master (feature branch)

- Numerická parita fall algoritmu proti legacy implementaci chráněna golden-master testy (`tests/fall_detection/golden/`)
- Testy: **46 passed** (2026-07-27, na `feature/mediamtx-input-profile`)

---

## Implementované, ale čekající na další integraci (IMPLEMENTED)

| Co | Kde | Poznámka |
|---|---|---|
| Fall detector aplikace (`apps/zahradky_horni_pad.py`) | detector `feature/zahradky-fall-detection` + následné větve | **Není na main.** Main detektoru obsahuje jen barrier baseline + modely (LFS). |
| MediaMTX input profil detektoru (`mediamtx_proxy`) | detector `feature/mediamtx-input-profile` (draft PR #2) | Detektor umí číst RTSP z MediaMTX `:8554`; ověřeno integračně (~8,3 FPS), nemergováno |
| EventCorePublisher (HTTP → Event Core, outbox/retry) | detector `feature/fall-event-core-integration` | Plná implementace na samostatné větvi; na aktuální pracovní větvi jen stub (`NotImplementedError`) |
| JSONL + Telegram publisher | detector feature větve | JSONL zapojen; Telegram volitelný přes env |
| Simulátor systému (`scripts/simulate_system.py`) | platform main | Nahrazuje detektor pro end-to-end testy alarmů |

## Experimentální (EXPERIMENTAL — draft PR, nemergováno)

| PR | Repo | Obsah |
|---|---|---|
| #10 | platform | Druhá kamera `10.2.4.90`: comparison paths `zahradky-horni-stanice-92`/`-90`, setup/verify/benchmark skripty. WHEP obou streamů ověřen (OPTIONS 204/POST 201/PATCH 204/ICE connected), 20min benchmark stabilní. Čeká na vizuální potvrzení compare stránky. |
| #9 | monitor | Compare stránka `/compare/zahradky-horni-stanice` — dvě nezávislé WHEP sessions vedle sebe |
| #9 | platform | Maintenance: WHEP-first restart/status skripty MediaMTX |
| #8 | monitor | Maintenance: monitor stop/status skripty, ochrana cizích procesů |
| #7 | platform | Starší internal-LAN WHEP profil (překryt merged PR #8) |
| #6 | monitor | `VITE_DEPLOYMENT_MODE=internal-lan` pro Lovable (překryt merged PR #7) |
| #5 | platform | Realtime camera registry + MediaMTX status endpoint |
| #2 | detector | MediaMTX input profil |
| #1 | detector | Chráněný fall detector baseline |

## Deferred (DEFERRED — záměrně odloženo, nemergovat)

| PR | Repo | Obsah |
|---|---|---|
| #6 | platform | `[DEFERRED]` Public HTTPS WHEP deployment (Caddy/TLS/veřejná IP) |
| #5 | monitor | `[DEFERRED]` Production env URLs pro public HTTPS WHEP |

Důvod: uživatel explicitně zvolil čistý internal-LAN provoz. Lovable.app jako produkční hosting selhal na Local Network Access (HTTPS stránka → HTTP LAN zdroj = `Failed to fetch`).

## Neimplementované (PLANNED)

- Detector jako trvalá služba čtoucí produkční MediaMTX path a publikující do Event Core (Phase 3–4)
- Produkční frontend build + statické servírování (bez Vite dev serveru) a server-side BFF (Phase 5)
- Auto-start služeb po rebootu, watchdog, centrální logy (Phase 6)
- Fyzické I/O: Advantech USB-4761, semafor, siréna — **fall detektor nesmí přímo ovládat relé bez definované safety logiky** (Phase 7)
- Dolní stanice: kamera + detektor + kiosk s videem (Phase 8)
- Auth pro GET/WS, zálohy DB, observabilita (Phase 9)

---

## Nesrovnalosti staré dokumentace vs. aktuální kód

1. **platform `contracts/websocket-events.md`** — `service.offline` je emitován i pro heartbeat s `status=offline`, ne jen pro timeout.
2. **monitor `docs/native-realtime-video.md`** — naznačuje, že `VITE_VIDEO_MODE=whep` funguje i v PROD buildu; kód v PROD **vynucuje `placeholder`**, pokud není `VITE_DEPLOYMENT_MODE=internal-lan`.
3. **monitor `docs/local-integration.md`** — staví na deprecated `VITE_VIDEO_POC_MODE=iframe`; runtime preferuje `VITE_VIDEO_MODE`.
4. **monitor WHEP kontrakt** — dokumentace připouští POST 200/201; klient vyžaduje **výhradně 201** (MediaMTX v1.11.3).
5. **detector `README.md` / `repo-structure.md`** — označují fall pad jako PLANNED / model missing; fall pad reálně existuje na feature větvích včetně modelu (LFS) a golden testů.
