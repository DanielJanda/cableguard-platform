# OPERATIONS — provozní runbook

Last verified: 2026-07-28

Verified against: production Chrome kiosk sprint

Vše se spouští na PC **`10.6.1.40`**. Skripty nikdy nevypisují secrets.

### Preferovaný admin vstup (po Phase 2.5 / 2.6)

```powershell
cd C:\Users\mega\Documents\cableguard-platform\tools\control-center
dotnet run --project src/CableGuard.ControlCenter
# nebo: publish\CableGuard.ControlCenter.exe
```

**CableGuard Admin Studio** (Control Center) — OPERATIONS = start/stop/health/logy/open monitor; TEST LAB = kamery, streams, detectors, ROI, Telegram, hardware test, scenarios. Lokální config jen v gitignored `runtime/config/`.

PowerShell skripty níže zůstávají platné jako fallback / CI.

---

## Start systému (produkce)

```powershell
cd C:\Users\mega\Documents\cableguard-platform
# 1) MediaMTX (služba CableGuardMediaMTX nebo start_mediamtx.ps1)
# 2) Production monitor (Event Core :8080 + Nitro UI :18080) — NE Vite
.\scripts\start_production_monitor.ps1

# 3) Chrome operator kiosk (jednou install, pak start / logon task)
.\scripts\manage_operator_kiosk.ps1 -Action install   # AutoplayAllowlist ideálně jako Admin
.\scripts\manage_operator_kiosk.ps1 -Action start
.\scripts\manage_operator_kiosk.ps1 -Action status
```

Kanonický origin: `$env:CABLEGUARD_PUBLIC_ORIGIN` (default `http://10.6.1.40:8080`). Stejný origin musí mít MediaMTX `webrtcAllowOrigin` (`patch_mediamtx_internal_lan.ps1`).

Dashboard:   http://10.6.1.40:8080/dashboard
Horní kiosk: http://10.6.1.40:8080/kiosk/zahradky/horni-stanice

Stop produkčního monitoru: `.\scripts\stop_production_monitor.ps1`

### Development / TEST LAB (Vite)

```powershell
.\scripts\start_internal_event_core.ps1   # :8000
# cableguard-monitor:
.\scripts\start_internal_monitor.ps1      # Vite :8080 + dev BFF
```

## Start systému (legacy one-shot)

Doporučený způsob — celý stack jedním skriptem (stále Vite monitor):

```powershell
cd C:\Users\mega\Documents\cableguard-platform
.\scripts\start_internal_cableguard.ps1
```

Skript idempotentně (bez duplicitních procesů) spustí/ověří:

1. **Event Core** na `0.0.0.0:8000` (`start_internal_event_core.ps1`)
2. **MediaMTX** s gitignored `deploy/mediamtx/mediamtx.local.yml` (`start_mediamtx.ps1`)
3. **Monitor** na `0.0.0.0:8080` (`cableguard-monitor\scripts\start_internal_monitor.ps1`, mode `internal-lan`)
4. firewall pravidla (přeskočí s varováním bez admin práv)

a vypíše provozní adresy:

```
Dashboard:   http://10.6.1.40:8080/dashboard
Historie:    http://10.6.1.40:8080/events
Horní kiosk: http://10.6.1.40:8080/kiosk/zahradky/horni-stanice
Přímý stream: http://10.6.1.40:8889/zahradky-horni-stanice/
```

Jednotlivé služby lze startovat i samostatně stejnými skripty.

## Stop systému

```powershell
# MediaMTX (podle PID file)
cd C:\Users\mega\Documents\cableguard-platform
.\scripts\stop_mediamtx.ps1

# Monitor — vylepšený managed stop je v draft PR #8 (monitor):
# .\scripts\stop_internal_monitor.ps1
# na main: ukončit okno/proces Vite dev serveru na portu 8080

# Event Core: ukončit uvicorn proces (Ctrl+C v jeho okně, nebo podle portu)
Get-NetTCPConnection -LocalPort 8000 -State Listen | ForEach-Object { Stop-Process -Id $_.OwningProcess }
```

## Status

```powershell
cd C:\Users\mega\Documents\cableguard-platform
.\scripts\status_mediamtx.ps1

# rychlý přehled portů
Get-NetTCPConnection -LocalPort 8000,8080,8889 -State Listen |
  Select-Object LocalAddress,LocalPort,OwningProcess
```

Vylepšené WHEP-first status/restart skripty (rozlišují „proces neběží“ / „port nenaběhl“ / „API nedostupné“ / „WHEP ready“) jsou v draft PR: platform #9, monitor #8.

## Restart MediaMTX

```powershell
.\scripts\stop_mediamtx.ps1
.\scripts\start_mediamtx.ps1
```

Očekávané chování v monitoru: kiosk zobrazí **OFFLINE/OBNOVUJI**, po náběhu MediaMTX se WHEP player automaticky reconnectne na **LIVE** (backoff 1–30 s). Ověřeno akceptačně.

## Restart monitoru

```powershell
# ukončit proces na 8080, poté:
cd C:\Users\mega\Documents\cableguard-monitor
.\scripts\start_internal_monitor.ps1
```

Skript kontroluje obsazenost portu 8080 a načítá gitignored `.env.internal-lan.local`.

## Restart Event Core

```powershell
# ukončit uvicorn na 8000, poté:
cd C:\Users\mega\Documents\cableguard-platform
.\scripts\start_internal_event_core.ps1
```

DB je persistentní (SQLite WAL) — restart nezahazuje eventy ani historii. WS klienti monitoru se automaticky reconnectnou (backoff max 15 s).

## Health checks

```powershell
# Event Core
Invoke-RestMethod http://10.6.1.40:8000/api/v1/health     # {ok: true}
Invoke-RestMethod http://10.6.1.40:8000/api/v1/status     # služby + open_events

# MediaMTX WHEP readiness (primární video health)
Invoke-WebRequest -Method Options http://10.6.1.40:8889/zahradky-horni-stanice/whep   # 204

# MediaMTX API (jen z lokálního PC, diagnostika)
Invoke-RestMethod http://127.0.0.1:9997/v3/paths/list

# Monitor
Invoke-WebRequest http://10.6.1.40:8080/dashboard   # 200
```

## Přímý test videa (bez monitoru)

V prohlížeči otevřít vestavěný MediaMTX player:

```
http://10.6.1.40:8889/zahradky-horni-stanice/
```

Živý obraz zde = kamera + MediaMTX + WHEP fungují; problém je pak ve frontend vrstvě.

## Test z druhého PC

Z jiného PC ve firemní LAN:

1. `http://10.6.1.40:8080/dashboard` — načte se UI
2. `http://10.6.1.40:8080/kiosk/zahradky/horni-stanice` — LIVE video 1280×720
3. `http://10.6.1.40:8889/zahradky-horni-stanice/` — přímý stream
4. volitelně automatizovaně z vývojového PC: `cd cableguard-monitor; npm run verify:internal-lan-whep` (OPTIONS 204, POST 201, PATCH 204)

## Simulace fall eventu

```powershell
cd C:\Users\mega\Documents\cableguard-platform
.\.venv\Scripts\python.exe scripts\simulate_system.py --scenario fall
# (viz --help; vyžaduje ingest key v env / .env)
```

Očekávání: v kiosku se otevře alarm overlay se zvukem, event přibude v `/events`.

## Acknowledge test

1. Vyvolat simulovaný fall event (výše).
2. V kiosku kliknout na potvrzení alarmu.
3. Ověřit: overlay zmizí, event má stav `acknowledged`:

```powershell
Invoke-RestMethod "http://10.6.1.40:8000/api/v1/events?status=acknowledged&limit=5"
```

4. Idempotence: opakované potvrzení stejného eventu nesmí vyvolat chybu (200/409→refetch).

## Co dělat, když video nejde (diagnostický strom)

Postupovat od zdroje k prohlížeči — v každém kroku se rozhodne, kde je závada:

1. **Camera source** — `.\scripts\status_mediamtx.ps1`; API `http://127.0.0.1:9997/v3/paths/list` → je path `ready: true` a má `source`? Ne → kamera/RTSP problém (síť do 10.2.4.x, credentials v `mediamtx.local.yml`, kamera restart).
2. **MediaMTX path** — path existuje, ale not ready → zkontrolovat logy v `runtime/mediamtx/`, případně `.\scripts\stop_mediamtx.ps1; .\scripts\start_mediamtx.ps1`.
3. **WHEP handshake** — `Invoke-WebRequest -Method Options http://10.6.1.40:8889/<path>/whep` → není 204? MediaMTX neběží nebo špatný `webrtcAllowOrigin` (musí být `http://10.6.1.40:8080`; oprava: `.\scripts\patch_mediamtx_internal_lan.ps1`).
4. **ICE** — handshake OK, ale video nenabíhá → UDP 8189 blokován firewallem (`.\scripts\ensure_internal_firewall.ps1` jako admin) nebo klient mimo LAN.
5. **Frontend** — přímý player (`:8889/<path>/`) funguje, kiosk ne → zkontrolovat `.env.internal-lan.local` (`VITE_WHEP_BASE_URL=http://10.6.1.40:8889`, `VITE_VIDEO_MODE=whep`, `VITE_DEPLOYMENT_MODE=internal-lan`, žádný BOM v souboru!) a restartovat monitor.

## Co dělat, když video funguje, ale alarm ne

1. **Detector/simulátor** — POST /events vrací 201/200? 401 → chybí/nesedí `CABLEGUARD_INGEST_API_KEY`. Connection refused → Event Core neběží.
2. **Event Core** — `GET /api/v1/events?limit=5` obsahuje event? Ne → problém v ingestu; ano → pokračovat.
3. **WebSocket** — stránka `/system` v monitoru ukazuje WS připojeno? Ne → `VITE_WS_URL=ws://10.6.1.40:8000/ws/v1` v env profilu + CORS Event Core.
4. **Frontend** — event chodí (vidět v `/events`), ale overlay se neotvírá → zkontrolovat, že event má `severity=alarm|critical` a `event_type` fall pro danou stanici; konzole prohlížeče.


## Zahrádky on-site commissioning

Post-merge checklist: [zahradky-onsite-commissioning-checklist.md](../operations/zahradky-onsite-commissioning-checklist.md). Not a software release blocker.
