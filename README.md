# CableGuard Platform

Event Core (FastAPI) + provozní skripty + MediaMTX + **Admin Studio**.

## Dokumentace — začni tady

**[`DOKUMENTACE.md`](DOKUMENTACE.md)** — jediný vstupní bod pro celý CableGuard (všechna 3 repa).

Kanonická sada: [`docs/project/README.md`](docs/project/README.md)

## Admin Studio

```powershell
.\tools\control-center\publish\CableGuard.ControlCenter.exe
```

## Quick start Event Core

```powershell
cd cableguard-platform
# nastav CABLEGUARD_INGEST_API_KEY a CABLEGUARD_KIOSK_API_KEY v .env
.\scripts\start_internal_event_core.ps1
```

Celý interní stack bez GUI: `.\scripts\start_internal_cableguard.ps1`

## Role

| Je | Není |
|---|---|
| Event Core, health, acknowledge, WS | YOLO / fall algoritmus (detector repo) |
| MediaMTX runtime + Admin Studio | Operátorské React UI (monitor repo) |
| Kanonická projektová dokumentace v `docs/project/` | — |
