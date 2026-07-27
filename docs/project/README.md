# CableGuard

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

Statusy používané v celé dokumentaci: **CONFIRMED** (ověřeno akceptačním testem), **IMPLEMENTED** (v kódu na main, bez plné akceptace), **EXPERIMENTAL** (feature branch / draft PR), **PLANNED** (neexistuje), **DEFERRED** (záměrně odloženo).

---

## Účel systému

CableGuard je **interní bezpečnostní systém pro dohled nad provozem lanových drah / lyžařských zařízení** (lokalita Zahrádky). Běží výhradně ve firemní LAN.

Hlavní funkce:

- realtime kamerový dohled (RTSP kamery → prohlížeč, sub-sekundová latence),
- AI detekce rizikových událostí (pád osoby, bezpečnostní zábrany) pomocí YOLO pose + trackingu,
- distribuce alarmů operátorům přes WebSocket,
- operátorské potvrzení alarmu (acknowledge) s idempotencí a auditní stopou,
- historie událostí a stav služeb (heartbeat/health),
- budoucí integrace fyzických signalizačních zařízení (semafor, siréna, relé Advantech USB-4761).

## Tři hlavní části

| Repo | Odpovědnost | Technologie | Co nesmí dělat |
|---|---|---|---|
| **cableguard-detector** | AI inference: YOLO pose, BotSORT tracking, fall/barrier risk klasifikace, publikace událostí | Python 3.10, Ultralytics 8.3.77, torch 2.5 (CUDA 11.8), OpenCV | Nesmí přímo ovládat frontend ani relé bez definované safety logiky; nesmí obsahovat video-distribuční logiku |
| **cableguard-platform** | Event Core (FastAPI): ingest událostí, persistence (SQLite), acknowledge, health/status, WebSocket distribuce; provozní skripty a MediaMTX konfigurace | Python 3.10, FastAPI, SQLAlchemy, Alembic, SQLite (WAL), MediaMTX v1.11.3 | Nesmí přenášet video; nesmí rozhodovat o riziku (to dělá detektor) |
| **cableguard-monitor** | Operátorské UI: dashboard, kiosky, historie, systémový přehled, nativní WHEP video player, BFF pro acknowledge | React 19, TanStack Start/Router, Vite 8, Tailwind 4, shadcn/radix | Nesmí obsahovat secrets (kiosk key jen server-side), nesmí provádět inference, nesmí znát RTSP credentials |

Umístění repozitářů (vývojové PC `10.6.1.40`):

```
C:\Users\mega\Documents\cableguard-platform
C:\Users\mega\Documents\cableguard-monitor
C:\Users\mega\Documents\cableguard-detector
```

## Dokumentace

| Dokument | Obsah |
|---|---|
| [CURRENT_STATE.md](CURRENT_STATE.md) | Co dnes skutečně funguje — ověřené vs. experimentální vs. deferred |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Logická architektura, čtyři plane, objektivní posouzení |
| [COMPONENTS.md](COMPONENTS.md) | Inventář všech komponent |
| [VIDEO_PIPELINE.md](VIDEO_PIPELINE.md) | Kamera → RTSP → MediaMTX → WHEP/WebRTC → prohlížeč |
| [EVENT_PIPELINE.md](EVENT_PIPELINE.md) | Event Core: REST, WebSocket, idempotence, persistence |
| [FALL_DETECTION.md](FALL_DETECTION.md) | Algoritmus pádové detekce, parametry, golden master |
| [NETWORK_AND_PORTS.md](NETWORK_AND_PORTS.md) | Autoritativní tabulka portů a bindů |
| [OPERATIONS.md](OPERATIONS.md) | Provozní runbook: start/stop/status/diagnostika |
| [DEVELOPMENT_WORKFLOW.md](DEVELOPMENT_WORKFLOW.md) | Git workflow, Lovable, deployment z main |
| [TESTING.md](TESTING.md) | Testovací pyramida a aktuální výsledky |
| [SECURITY.md](SECURITY.md) | Secrets, trusted-LAN model, omezení |
| [DECISIONS.md](DECISIONS.md) | Architecture Decision Log (ADR-001 … ADR-008) |
| [RISKS_AND_TECH_DEBT.md](RISKS_AND_TECH_DEBT.md) | Kritické zhodnocení rizik a technického dluhu |
| [ROADMAP.md](ROADMAP.md) | Fázovaný plán vývoje + projektový dashboard |
