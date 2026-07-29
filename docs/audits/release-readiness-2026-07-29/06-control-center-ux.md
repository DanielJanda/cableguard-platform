# 06 — Control Center functional & UX audit

**Code tip reviewed:** PR #34 `feat/control-center-operations-ux` (`afed13e`) via source inventory.  
**Running tip caveat:** local platform checkout often on PR #35 **without** these CC changes.

**Runtime click-through of every button:** **NOT completed** in this freeze (would require published #34 binary + safe office session). Status below is **code/UI inventory + prior office evidence**, not a full PASS matrix.

## Action inventory (selected)

| Obrazovka | Prvek | Backend | Loading/Success/Error | Stav |
|-----------|-------|---------|----------------------|------|
| Přehled | START PRODUKCE / STOP VŠE | process manager + scripts | present in VM | PARTIAL — verify on #34 publish |
| Přehled | Office fall Start ± debug | DetectorLaunchBuilder PyAV | env soft-fail | PARTIAL |
| Přehled | MONITOR / KIOSK / STREAM PREVIEW | shell open URLs | — | PARTIAL |
| Scénáře | kancelářský test pádu | launcher | — | PARTIAL |
| Kamery | CRUD + Apply MTX + credentials | cameras.json + CredMan + MTX API | — | PARTIAL |
| Detektory | Start/Stop/Náhled | process manager | — | PARTIAL |
| Recording helpers | enable/status scripts | MTX API/yml | docs audit gate1 | PARTIAL (#33⊂#34) |
| Hardware | relé / bzučák | USB-4761 | TEST MODE flag | PASS WITH NOTES — dangerous if misused |
| Video laboratoř | soak/inject/qualify | lab services | — | PARTIAL — advanced |
| Notifikace | Telegram test | CredMan | — | PARTIAL |
| Kalibrace ROI | snapshot/ROI save | roi json | — | PARTIAL |
| Systém | save settings | local settings | — | PARTIAL |

Any control without verified backend on the **running** binary must be treated as **UI PLACEHOLDER — NOT IMPLEMENTED** for that binary.

## Role assessment

| Role | Need | Current UI | Recommendation |
|------|------|------------|----------------|
| Technik | run/stop/test/simple error | Buried among lab metrics | **SIMPLIFY** Overview |
| Admin | config, recording, logs, export | Spread across tabs | **KEEP** tabs; tighten Overview |
| Developer | codec, PTS, session, soak | Video Lab on main window | **MOVE TO ADVANCED** |

## Proposed navigation (advisory only — not implemented)

Overview · Cameras · Detection · Recording · Events & Tests · Logs · Settings · **Advanced/Lab**

Overview should show: site/lift, overall state, cameras, detectors, recording, Event Core, Monitor, disk, errors, START ALL / STOP ALL / OTESTOVAT — **not** raw latency grids.

## UX directives

| Item | Directive |
|------|-----------|
| START ALL / STOP ALL | KEEP (confirm STOP) |
| Office camera default | KEEP |
| Debug overlay as primary start | SIMPLIFY — secondary |
| Video Lab soak inject | MOVE TO ADVANCED |
| Hardware full panel on Overview | MOVE / MERGE under Hardware |
| Duplicate Start bez/s náhledem clutter | SIMPLIFY copy |
| Per-service log buttons | KEEP |
| Dense metric strips | SIMPLIFY / MOVE TO ADVANCED |

## Verdict

**Control Center is capable but not release-clean.** Critical office workflow exists in code on **#34**, but branch/tip confusion and incomplete freeze click-matrix ⇒ **PARTIAL**. Do not treat Video Lab as operator-ready.
