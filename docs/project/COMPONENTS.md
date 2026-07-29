# COMPONENTS — inventář komponent

Last verified: 2026-07-28

Verified against: production Chrome kiosk sprint

---

| Komponenta | Repository | Technologie | Entrypoint | Odpovědnost | Vstupy | Výstupy | Konfigurace | Stav | Test coverage |
|---|---|---|---|---|---|---|---|---|---|
| **MediaMTX** | platform (`deploy/mediamtx/`, binárka v gitignored `runtime/mediamtx/`) | MediaMTX v1.11.3 (Go, single binary) | `scripts/start_mediamtx.ps1` | Video router: RTSP ingest → WHEP egress + RTSP proxy | RTSP kamery (TCP) | WHEP `:8889`, ICE UDP `:8189`, RTSP `:8554`, HLS `:8888`, API `127.0.0.1:9997` | `mediamtx.local.yml` (gitignored, RTSP creds), `mediamtx.example.yml` (šablona) | CONFIRMED | `status_mediamtx.ps1`, `test_mediamtx_stream.ps1` |
| **Event Core** | platform (`backend/app/`) | FastAPI, Python 3.10, uvicorn | `scripts/start_internal_event_core.ps1` | Ingest, persistence, ack, health, WS distribuce | REST (detector/simulátor, kiosk), heartbeaty | REST JSON, WS `/ws/v1` | env `CABLEGUARD_*` (`.env` gitignored) | CONFIRMED | pytest **37 passed** (7 souborů) |
| **SQLite** | platform (`data/cableguard.sqlite3`, gitignored) | SQLite WAL + SQLAlchemy + Alembic | — (spravuje Event Core) | Persistence: events, service_health, status_history, acknowledgements | ORM zápisy | ORM čtení | `CABLEGUARD_DATABASE_URL`; migrace `0001`, `0002` | CONFIRMED | migrace spouštěny v test fixtures |
| **WebSocket** | platform (`api/v1/websocket.py`, `services/websocket_manager.py`) | FastAPI WS | `WS /ws/v1` | Realtime push: snapshot + 4 typy zpráv | interní publish z services | envelope `{type, data, sent_at}` | — (bez auth, trusted LAN) | CONFIRMED | `test_realtime_and_health.py` |
| **BFF** | platform (`backend/app/api/bff.py`) | FastAPI | Event Core / produkční `:8080` | Server-side acknowledge bez kiosk key v browseru | `POST /bff/events/{id}/acknowledge` | stejná logika jako `/api/v1/.../acknowledge` | `CABLEGUARD_KIOSK_API_KEY` | CONFIRMED | `test_bff_and_spa.py` |
| **React monitor** | monitor (`src/`) | React 19, TanStack Start/Nitro | produkce: `build:production-lan` + platform `start_production_monitor.ps1`; dev: `start_internal_monitor.ps1` | Dashboard, events, system, kiosky, audio self-test | REST + WS + WHEP | UI | `VITE_*` / `.env.production-lan` | CONFIRMED | `verify:*` + audio-gate |
| **WHEP player** | monitor (`src/services/whepClient.ts`, `src/hooks/useVideoStream.ts`, `src/components/VideoPlayer.tsx`) | WebRTC/WHEP (nativní, bez knihovny) | — (komponenta) | OPTIONS/POST 201/PATCH 204/DELETE, ICE, reconnect s backoff 1–30 s, diagnostika | WHEP endpoint MediaMTX | `<video>` stream + `WhepDiagnostics` | `VITE_WHEP_BASE_URL`, `VITE_VIDEO_MODE` | CONFIRMED | `verify-whep-player.test.mjs`, `verify-internal-lan-whep.mjs` (live) |
| **Fall detector** | detector (`src/cableguard/detection/pad/`, `apps/zahradky_horni_pad.py`) | Python 3.10, Ultralytics | `python apps/zahradky_horni_pad.py --mode ... --input-profile ...` | Fall risk score, state machine, emit-once | pose keypoints, ROI, movement history | fall event → publishers | `sites/zahradky/horni_pad.yaml` | IMPLEMENTED (feature branch, **není na main**) | golden master + unit, **46 passed** |
| **YOLO Pose** | detector (`models/shared/yolo11m-pose.pt`, Git LFS) | YOLO11m-pose, imgsz=480, CUDA half / CPU fallback | — (načítá app) | Pose estimation osob (class 0) | frame | keypoints + boxes | `models-manifest.json` (SHA256), `MODEL_FALL_POSE_PATH` | IMPLEMENTED | manifest test (`test_models.py`) |
| **BotSORT** | detector (přes `model.track(persist=True)`) | Ultralytics BotSORT (`botsort.yaml`), `lap` | — | Track ID persistence mezi framy | detekce | tracky s ID | `tracker:` v `horni_pad.yaml` | IMPLEMENTED | nepřímo přes golden master |
| **Jsonl publisher** | detector (`src/cableguard/events/publishers.py`) | Python | — (zapojen v app) | Append fall eventů do `runtime/.../fall_events.jsonl` | fall event | JSONL řádky | cesta z runtime konfigurace | IMPLEMENTED | `test_algorithm.py` |
| **Telegram publisher** | detector (tamtéž) | Python + Telegram Bot API | — (volitelný) | Notifikace do Telegramu | fall event | zpráva | `TELEGRAM_ENABLED` + token/chat v env (gitignored) | IMPLEMENTED (volitelný) | bez testu |
| **EventCorePublisher** | detector (`src/cableguard/events/event_core/` na `feature/fall-event-core-integration`) | Python + httpx, outbox/retry | — | HTTP publikace do `POST /api/v1/events` + heartbeaty | fall event | REST volání s `X-API-Key` | env (ingest key) | EXPERIMENTAL (samostatná větev; na aktuální pracovní větvi jen stub) | testy na dané větvi |
| **USB/relay subsystem** | detector (`advantech_relay.py`, `relay_server.py`, `zahradky_safety/`) | pythonnet + Advantech USB-4761 | `relay_server.py` | Fyzické relé pro barrier detektory (ne fall) | detekční signály barrier | relé sepnutí | env + safety-invariants.md | IMPLEMENTED (barrier produkce, mimo fall scope) | `test_fake_relay_client.py` |
| **Admin Studio (Control Center)** | platform (`tools/control-center/`) | C# / .NET 8 / WPF | `CableGuard.ControlCenter.exe` | Lokální admin/test GUI: OPERATIONS + TEST LAB + **Video Lab** (metrics, manual G2G, soak, qualification) | health probes, PowerShell scripts, MediaMTX API, localhost metrics `:9998`, gitignored `runtime/config/` | start/stop, stream switch+rollback, detector launch, Video Lab reports | examples v `tools/control-center/config/` | IMPLEMENTED (2.5/2.6 DONE; 2.6b Video Lab in progress) | xUnit (63 tests) |

## Fall detector video backend

Default production profile: `pyav_rtsp` + `mediamtx`. Control Center Overview can show Event Core `video_input` health when heartbeats are enabled.
