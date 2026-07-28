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
.\.venv\Scripts\Activate.ps1
# nastav CABLEGUARD_INGEST_API_KEY a CABLEGUARD_KIOSK_API_KEY v .env
cd backend
alembic upgrade head
uvicorn app.main:app --host 0.0.0.0 --port 8000
```

Celý interní stack: `.\scripts\start_internal_cableguard.ps1`

## Role

| Je | Není |
|---|---|
| Event Core, health, acknowledge, WS | YOLO / fall algoritmus (detector repo) |
| MediaMTX runtime + Admin Studio | Operátorské React UI (monitor repo) |
| Kanonická projektová dokumentace | Legacy soubory v `docs/*.md` mimo `project/` |
