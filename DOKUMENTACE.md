# CableGuard — dokumentace (jediný vstupní bod)

**Čti nejdřív tento soubor.** Všechno ostatní je buď kanonická sada níže, nebo zastaralé / repo-specifické doplňky.

Umístění repozitářů (`10.6.1.40`):

```
C:\Users\mega\Documents\cableguard-platform   ← Event Core, MediaMTX, Admin Studio, **kanonická dokumentace**
C:\Users\mega\Documents\cableguard-monitor    ← operátorské UI
C:\Users\mega\Documents\cableguard-detector   ← AI detekce
```

---

## Co číst (minimum)

| Pořadí | Soubor | Kdy |
|---|---|---|
| 1 | [`docs/project/CURRENT_STATE.md`](docs/project/CURRENT_STATE.md) | Co dnes opravdu funguje |
| 2 | [`docs/project/OPERATIONS.md`](docs/project/OPERATIONS.md) | Start / stop / diagnostika |
| 3 | [`docs/project/ROADMAP.md`](docs/project/ROADMAP.md) | Co je další fáze |
| 4 | [`tools/control-center/README.md`](tools/control-center/README.md) | Admin Studio (lokální GUI) |

Admin Studio EXE: `tools\control-center\publish\CableGuard.ControlCenter.exe`

---

## Kanonická sada (jedna složka)

Celá projektová dokumentace žije **jen** tady:

**[`docs/project/`](docs/project/README.md)**

Index té složky: [`docs/project/README.md`](docs/project/README.md)

| Téma | Soubor |
|---|---|
| Stav | CURRENT_STATE |
| Architektura / komponenty | ARCHITECTURE, COMPONENTS |
| Video / eventy / detekce | VIDEO_PIPELINE, EVENT_PIPELINE, FALL_DETECTION |
| Síť / provoz / vývoj | NETWORK_AND_PORTS, OPERATIONS, DEVELOPMENT_WORKFLOW |
| Testy / bezpečnost / rizika / rozhodnutí | TESTING, SECURITY, RISKS_AND_TECH_DEBT, DECISIONS |
| Plán | ROADMAP |

**Nehledej projektovou pravdu** ve složkách `cableguard-monitor/docs` ani `cableguard-detector/docs` — ty mají jen úzké repo-specifické poznámky a odkazují sem.

---

## Zastaralé / duplicitní

| Kde | Stav |
|---|---|
| `cableguard-platform/docs/*.md` (mimo `project/`) | **LEGACY** — přesměrování, neaktualizovat |
| `cableguard-detector/docs/*` (kromě README + fall/installation) | mapa / historie; kanonický stav je v platform `docs/project/` |
| `cableguard-monitor/docs/*` | integrační poznámky; kanonické video je VIDEO_PIPELINE |

---

## Rychlé příkazy

```powershell
# Celý stack
cd C:\Users\mega\Documents\cableguard-platform
.\scripts\start_internal_cableguard.ps1

# Admin Studio
.\tools\control-center\publish\CableGuard.ControlCenter.exe
```

Náhled AI detekce: v Admin Studio → **DETEKCE S NÁHLEDEM** (okno OpenCV, ne monitor).
