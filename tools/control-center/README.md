# CableGuard Admin Studio (Control Center)

Lokální **administrační / testovací GUI** pro správce a vývojáře na hlavním CableGuard PC (`10.6.1.40`).

- **Není** operátorský monitor (`cableguard-monitor`).
- **Není** Supervisor Service (Phase 6).
- Architektura: **Admin / Test plane** nad Video / Detection / Event / Presentation planes — nesmí měnit fall algorithm thresholds (golden master) ani automaticky vazbit detector → relay.

## Režimy

| OPERATIONS | TEST LAB |
|---|---|
| START/STOP stack, health, logy, open monitor/kiosk | kamery CRUD, streams, detector instances, ROI, Telegram, hardware TEST MODE, scenarios |
| nic citlivého se nepřenastavuje | banner **TEST MODE – změny mohou ovlivnit běžící systém** |

## Tabs

Overview · Scenarios · Cameras · Streams · Detectors · Calibration · Notifications · Hardware · Logs · System

## Runtime config (gitignored)

Vše pod `runtime/config/` — **nikdy secrets, nikdy Git commit lokálních IP**:

| Soubor | Účel |
|---|---|
| `cameras.json` | fyzické kamery |
| `streams.json` | logical MediaMTX paths → camera_id |
| `detectors.json` | detector instances |
| `roi/*.json` | ROI polygony |
| `scenarios.json` | test scenarios |
| `notifications.json` | Telegram flags (credential_ref only) |
| `hardware.json` | USB-4761 / relay settings |

Tracked examples: `tools/control-center/config/*.example.json`

Credentials: **Windows Credential Manager**.

## Detector launch

Control Center generuje launch spec (env + args) bez zásahu do algoritmu:

- Fall: `python apps/zahradky_horni_pad.py --input-profile mediamtx_proxy` (+ volitelně `--debug-overlay`)
- Barrier: `python zahradky_safety/app.py --mode production`
- Vstup = **logical MediaMTX stream** (`rtsp://127.0.0.1:8554/<path>`), ne fyzická IP kamery
- Fall entrypoint vyžaduje detector feature branch (není na `main`) — GUI hlásí chybějící script poctivě

## Inference preview

Produkční MediaMTX stream zůstává čistý. Debug = OpenCV `--debug-overlay` okno spuštěné z Detectors → OPEN DEBUG VIEW (localhost-only). Embedded WPF preview je follow-up.

## Hardware

Tab Hardware je **TEST MODE only**. Adapter na main zatím **NOT AVAILABLE** (žádný fake CONNECTED). Až bude wired na `relay_server` / Advantech: confirmation, pulse clamp, auto-off, audit log. **Žádná auto vazba fall → relay.**

## Spuštění

```powershell
cd tools/control-center
dotnet run --project src/CableGuard.ControlCenter
dotnet publish src/CableGuard.ControlCenter -c Release -o publish
dotnet test
```
