# CableGuard — dokumentace (jediný vstupní bod)

**Čti nejdřív tento soubor.** Všechno ostatní je buď kanonická sada níže, nebo repo-specifický doplněk.

Umístění repozitářů (`10.6.1.40`):

```
C:\Users\mega\Documents\cableguard-platform   ← Event Core, MediaMTX, Admin Studio, **kanonická dokumentace**
C:\Users\mega\Documents\cableguard-monitor    ← operátorské UI
C:\Users\mega\Documents\cableguard-detector   ← AI detekce
```

---

## Tři části a kde je jejich produkční kód

| Část | Repo | Produkční kód | Nesmí obsahovat |
|---|---|---|---|
| **Detekce** | `cableguard-detector` | `src/cableguard/` (sdílená knihovna), `apps/zahradky_horni_pad.py` (pád), `zahradky_safety/` (zábrany), `relay_server.py` (jediný držitel USB) | video distribuci, frontend |
| **Platforma** | `cableguard-platform` | `backend/app/` (Event Core), `tools/control-center/src/` (Admin Studio), `scripts/` (lifecycle skripty), `deploy/mediamtx/` | YOLO / rozhodování o riziku |
| **UI** | `cableguard-monitor` | `src/` (React 19 + TanStack) | secrets, inference, RTSP credentials |

---

## Jak se každá služba spustí

**Kanonický launcher je Admin Studio.** Skripty níže jsou implementace, kterou Admin Studio volá — spouštěj je ručně jen při diagnostice.

```powershell
.\tools\control-center\publish\CableGuard.ControlCenter.exe
```

| Služba | Kanonický entrypoint | Volá |
|---|---|---|
| MediaMTX | Admin Studio → MediaMTX Start/Stop/Restart | `scripts/start_mediamtx.ps1`, `scripts/stop_mediamtx.ps1` |
| Event Core | Admin Studio → Event Core Start/Stop/Restart | `scripts/start_internal_event_core.ps1` |
| Monitor | Admin Studio → Monitor Start/Stop/Open | `cableguard-monitor/scripts/start_internal_monitor.ps1` |
| Fall detector | Admin Studio → Detektory (Start / Debug ON-OFF) | `detector/apps/zahradky_horni_pad.py` |
| Barrier detector | Admin Studio → Detektory | `detector/zahradky_safety/app.py`, `.../safety_bar_spodni_kamera/app_relay.py` |
| USB-4761 | Admin Studio → Hardware (read-only, TEST MODE pro zápis) | `AdvantechUsb4761Adapter` → `BioDaqNativeSession` (nativní `Automation.BDaq4`) |

Scénáře v Admin Studiu jsou **pouze kombinace** těchto akcí, ne druhá implementace lifecyclu.

CLI fallback pro celý stack bez GUI: `.\scripts\start_internal_cableguard.ps1`
(skládá tytéž skripty + firewall + health check).

Jednorázový setup: `setup_mediamtx_local_config.ps1`, `patch_mediamtx_internal_lan.ps1`, `ensure_internal_firewall.ps1`.
Servisní/vývojové: `status_mediamtx.ps1`, `test_mediamtx_stream.ps1`, `reset_development_database.ps1`, `simulate_system.py`.

---

## Kde je konfigurace (jedno autoritativní místo na oblast)

| Oblast | Autoritativní zdroj | Poznámka |
|---|---|---|
| Kamery | `platform/runtime/config/cameras.json` | gitignored; šablona `tools/control-center/config/cameras.example.json` |
| Logické streamy | `platform/runtime/config/streams.json` | |
| MediaMTX paths | `platform/deploy/mediamtx/mediamtx.local.yml` | **generováno** z registru kamer, obsahuje RTSP credentials — nikdy necommitovat |
| Detector instance | `platform/runtime/config/detectors.json` | platforma předává detektoru přes CLI/env |
| ROI | `platform/runtime/config/roi/<id>.json` | |
| Test stations | `platform/runtime/config/test-stations.json` | |
| Scénáře | `platform/runtime/config/scenarios.json` | |
| Notifikace | `platform/runtime/config/notifications.json` | |
| Relay mapping | `platform/runtime/config/hardware.json` | viz [`docs/project/HARDWARE_RELAY.md`](docs/project/HARDWARE_RELAY.md) |
| Konstanty fall algoritmu | `detector/src/cableguard/detection/pad/` | zdrojový kód, ne runtime config |
| Site / lokalita | `detector/sites/<lokalita>/*.yaml` | |
| Model registry | `detector/models/models-manifest.json` | SHA-256 kontrola |
| Frontend prostředí | `monitor/.env.internal-lan.local` | gitignored; šablona `.env.internal-lan.example` |

Vše v `platform/runtime/` je gitignored a **nesmí se mazat** (kamery, ROI, hardware mapping, credentials, SQLite DB v `data/`).

---

## Kanonická sada dokumentace

Celá projektová dokumentace žije **jen** v [`docs/project/`](docs/project/README.md).

| Téma | Soubor |
|---|---|
| Stav | CURRENT_STATE |
| Architektura / komponenty | ARCHITECTURE, COMPONENTS |
| Video / eventy / detekce | VIDEO_PIPELINE, EVENT_PIPELINE, FALL_DETECTION |
| Hardware | HARDWARE_RELAY |
| Síť / provoz / vývoj | NETWORK_AND_PORTS, OPERATIONS, DEVELOPMENT_WORKFLOW |
| Testy / bezpečnost / rizika / rozhodnutí | TESTING, SECURITY, RISKS_AND_TECH_DEBT, DECISIONS |
| Plán | ROADMAP |

`cableguard-monitor/docs` a `cableguard-detector/docs` obsahují jen úzké repo-specifické poznámky a odkazují sem.

---

## Rychlé příkazy

```powershell
# Admin Studio (kanonické)
cd C:\Users\mega\Documents\cableguard-platform
.\tools\control-center\publish\CableGuard.ControlCenter.exe

# Celý stack bez GUI
.\scripts\start_internal_cableguard.ps1
```

Náhled AI detekce: v Admin Studio → **DETEKCE S NÁHLEDEM** (okno OpenCV, ne monitor).
