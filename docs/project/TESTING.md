# TESTING — testovací pyramida a aktuální výsledky

Last verified: 2026-07-27 (test suity znovu spuštěny tento den)

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f` (detector testy běženy na `feature/mediamtx-input-profile`, main fall testy neobsahuje)

---

## Testovací matice

| Vrstva | Co ověřuje | Kde | Automatizace |
|---|---|---|---|
| **Unit** | algoritmy, geometrie, idempotence, timestampy | platform pytest, detector pytest | plná |
| **Contract** | REST/WS/WHEP kontrakty, secrets v buildu | monitor verify skripty, platform pytest | plná (statická) |
| **Integration** | Event Core end-to-end (DB+WS), detector smoke video | platform pytest, detector `@integration` | plná / podmíněná (video soubor) |
| **Video** | živý WHEP handshake + frames | `verify-internal-lan-whep.mjs`, benchmark skripty | poloautomatická (vyžaduje běžící runtime) |
| **Acceptance** | druhý PC v LAN: dashboard, kiosk, LIVE, alarm, ack, reconnect | manuální checklist | manuální |
| **Manual operational** | restart MediaMTX → OFFLINE → LIVE, výpadek kamery, firewall | OPERATIONS.md postupy | manuální |

## Skutečné příkazy

### Platform (unit + contract + integration)

```powershell
cd C:\Users\mega\Documents\cableguard-platform
$env:PYTHONPATH = "$PWD\backend"
.\.venv\Scripts\python.exe -m pytest backend/tests -q
```

**Výsledek 2026-07-27: `37 passed, 1 warning in 6.06s`**

Pokrytí: API + auth (`test_api.py`), acknowledge idempotence (`test_acknowledgements.py`), WS realtime (`test_realtime_and_health.py`), UTC timestampy (`test_timestamps.py`), secrets vynucení (`test_secrets.py`), heartbeat kontrakt (`test_heartbeat_contract.py`).

### Monitor (contract, statické)

```powershell
cd C:\Users\mega\Documents\cableguard-monitor
npm run verify:whep        # WHEP klient: OPTIONS/POST 201/PATCH/DELETE/backoff, StrictMode guard
npm run verify:contracts   # API kontrakt, BFF path, žádný X-Kiosk-Key v klientu
npm run verify:secrets     # build + scan .output na klíče a rtsp://
```

**Výsledek 2026-07-27: všechny 3 PASS** (`verify:secrets` zahrnuje produkční build).

Doplňkově: `npx tsc --noEmit`, `npm run build`.

### Monitor (video, vyžaduje běžící runtime na 10.6.1.40)

```powershell
npm run verify:internal-lan-whep   # živě: OPTIONS 204, POST 201, PATCH 204 přes Playwright/Edge
```

**Naposledy PASS 2026-07-22** (při acceptance internal-lan runtime). 2026-07-27 neběženo — runtime aktuálně zastaven; test má smysl jen proti běžícímu stacku.

### Detector (unit + golden master)

```powershell
cd C:\Users\mega\Documents\cableguard-detector
$env:PYTHONPATH = "$PWD\src"
.\.venv\Scripts\python.exe -m pytest tests/fall_detection tests/common -q -m "not integration"
```

**Výsledek 2026-07-27: `46 passed, 1 deselected in 0.39s`** (deselected = `test_smoke_video.py`, vyžaduje `APP_DEV_VIDEO_PATH`).

Golden master (`test_golden_algorithm.py`) je součástí — numerická parita fall algoritmu vs. legacy potvrzena.

### Benchmark / porovnání kamer (EXPERIMENTAL, PR #10)

```powershell
cd C:\Users\mega\Documents\cableguard-platform
.\scripts\verify_dual_camera_paths.ps1          # ready + WHEP všech 3 paths
.\scripts\compare_cameras_benchmark.ps1 -DurationMinutes 20   # bez prohlížeče
node .\scripts\compare_cameras_benchmark.mjs    # WHEP přes Playwright/Edge
```

Poslední běh (2026-07-22): obě kamery stabilní 20 minut, 0 reconnectů.

### Second-PC LAN acceptance (manuální)

Checklist v OPERATIONS.md („Test z druhého PC“). **Naposledy potvrzeno uživatelem 2026-07-22**: dashboard, kiosk, LIVE 1280×720, alarm, acknowledge, restart MediaMTX → OFFLINE → automatický LIVE.

## Známá omezení testů

- **Headless WebRTC:** Playwright/headless Chromium neumí plný ICE/dekódování spolehlivě — živé WHEP testy používají reálný Edge s persistent kontextem a i tak občas visí (nutný timeout/kill). Vizuální potvrzení člověkem zůstává součástí acceptance.
- **Video-level parita detektoru** (frame pipeline vs. legacy) není automatizovaná — golden master je čistě numerický.
- `verify:secrets` skenuje jen `.output` bundle — server-side kód (BFF) klíč obsahovat smí.
