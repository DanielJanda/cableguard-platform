# CableGuard Control Center

Lokální **administrační GUI** pro správce na hlavním CableGuard PC (10.6.1.40).

- **Není to operátorský monitor** — ten je `cableguard-monitor` (React, :8080).
- **Není to Supervisor Service** — autonomní služby jsou Phase 6. Control Center nesmí být podmínkou běhu systému.
- Viz ADR-009 a Phase 2.5 v `docs/project/`.

## Co umí (MVP)

- Health-based stav služeb (MediaMTX / Event Core / Monitor / Fall Detector) — ne jen existence procesu:
  - MediaMTX: proces (PID file) + WHEP OPTIONS + path READY přes Control API
  - Event Core: proces/port + `GET /api/v1/health`
  - Monitor: proces + HTTP :8080
  - Detector: proces (command-line hint); hlubší health je poctivě **NOT AVAILABLE** do Phase 4
- START ALL v pořadí MediaMTX → Event Core → Monitor → Detector s readiness čekáním; při chybě `FAILED AT: <komponenta>` a stop
- Start / Stop / Restart / Logs per komponenta (re-use existujících PowerShell skriptů; stop přes managed PID file + taskkill /T)
- Logs tab: live tail, filtr, Errors only, clear view (soubor se nemaže), open folder; **všechny řádky procházejí redakcí secrets**
- Cameras tab: registry, LIVE/OFFLINE stav, Test connection, Preview (MediaMTX built-in player v prohlížeči), Enable/Disable, credentials (Windows Credential Manager), **Set as primary** s validací, WHEP ověřením a automatickým rollbackem
- Open Dashboard / Open Kiosk

## Spuštění (vyžaduje .NET 8 SDK)

```powershell
cd tools/control-center
dotnet run --project src/CableGuard.ControlCenter
```

Build samostatného EXE:

```powershell
dotnet publish src/CableGuard.ControlCenter -c Release -o publish
# → publish/CableGuard.ControlCenter.exe
```

## Testy

```powershell
dotnet test tests/CableGuard.ControlCenter.Tests
```

## Konfigurace (gitignored, v runtime/)

| Soubor | Účel |
|---|---|
| `runtime/config/controlcenter.json` | cesty k repos, LAN host, detector start command (Settings tab) |
| `runtime/config/cameras.json` | camera registry — **nikdy neobsahuje hesla** (validace to vynucuje); šablona v `config/cameras.example.json` |

Hesla kamer žijí ve **Windows Credential Manageru** pod `credential_ref` (např. `CableGuard.Camera.zahradky-upper-92`).

## Stream mapping

Fyzická kamera (`zahradky-upper-92` → 10.2.4.92) je oddělena od logického streamu (`zahradky-horni-stanice`). Frontend a detektor používají stabilní logickou path; přepnutí primární kamery mění pouze `source` logické path v MediaMTX (Control API `PATCH /v3/config/paths/patch/{name}` na 127.0.0.1:9997, ověřeno pro pinned v1.11.3) a po ověření READY+WHEP se persistuje do gitignored `deploy/mediamtx/mediamtx.local.yml` (s `.bak` zálohou). Při selhání se přes API vrátí původní source.

## Safety omezení

Control Center **neumí a nesmí**: měnit fall detection thresholds, ovládat relé/semafor, měnit AI model ani safety logiku. Jde výhradně o runtime administraci a video source management.
