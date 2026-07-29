# 02 — Feature catalog

Status legend: PASS | PASS WITH NOTES | PARTIAL | FAIL | NOT IMPLEMENTED | DEFERRED ON-SITE | OBSOLETE | UNKNOWN

Evidence codes: `code:` path · `test:` command · `runtime:` observation · `shot:` path · `api:` response

## A. VIDEO

| ID | Funkce | Repo | Implementace | UI | Test | Docs | Stav | Důkaz | Další krok |
|----|--------|------|--------------|----|------|------|------|-------|-----------|
| V01 | kamera → MediaMTX | platform | local yml paths | CC Apply MTX | scripts/status | VIDEO_PIPELINE | PASS WITH NOTES | runtime: 6 paths ready 2026-07-29 | keep example.yml credential-free |
| V02 | WHEP live view | monitor+MTX | whepClient+VideoPlayer | /live, kiosk | verify:whep; internal-lan-whep | native-realtime-video | PASS WITH NOTES | shot: monitor gate2 layout-3; runtime Monitor:8081 | document webrtcAllowOrigin |
| V03 | MediaMTX RTSP proxy | platform | path source rtsp | — | status_mediamtx | VIDEO_PIPELINE | PASS WITH NOTES | runtime paths ready | — |
| V04 | PyAV detector ingest | detector | pyav_rtsp_reader | CC on #34 | unit+office soak docs | fall-detector-stream-profiles | PARTIAL | code: detector PR#7; main lacks full stack | merge/align #7 |
| V05 | direct camera PyAV fallback | detector | office63_direct / pyav_direct | — | spike audits | audits/spike-pyav… | PARTIAL | hardcoded office path to platform local yml | isolate office profile |
| V06 | latest-frame slot | detector | LatestFrameHolder PR#5 | — | test_latest_frame | audits | PARTIAL | PR#5 not in #7 ancestry | reconcile with PyAV path |
| V07 | reconnect | detector+MTX | pyav reconnect smoke; whep backoff | tile RECONNECTING | tools/diagnostics | — | PASS WITH NOTES | code + smoke tool | E2E S10 |
| V08 | stale-frame handling | detector+monitor | freshness metrics; tile STALE | CameraTile | — | multi-camera-live-view | PARTIAL | code: CameraTile 8s STALE | unify detector+UI |
| V09 | debug overlay | detector | async_debug_display / lightweight overlay | OpenCV window | overlay audits | audits | PASS WITH NOTES | PR#6⊂#7 | keep off ops UI |
| V10 | rolling recording | platform/MTX | fMP4 record in local yml | CC recording on #33/#34 | gate1 recording audit doc | PR#33 docs | PARTIAL | runtime/recordings exists; not on main tip #35 | merge #34 stack |
| V11 | playback API | MTX | playback :9996 localhost | — | local evidence | NETWORK | PARTIAL | local only | document ops |
| V12 | auto cache delete | MTX | recordDeleteAfter | — | — | — | PARTIAL | config-driven | verify S08 |

## B. DETECTION

| ID | Funkce | Repo | Stav | Důkaz | Další krok |
|----|--------|------|------|-------|-----------|
| D01 | YOLO pose | detector | PASS WITH NOTES | code+pytest 116 | — |
| D02 | BotSORT | detector | PASS WITH NOTES | ultralytics tracker yaml | pin tracker file |
| D03 | ROI | detector | PASS WITH NOTES | horni_pad.yaml + barrier hardcoded | unify ROI owner |
| D04 | fall risk + SM | detector | PASS WITH NOTES | golden master hashes recorded | do not regen |
| D05 | event transition | detector | PARTIAL | Event Core publisher opt-in default off | enable via env in CC |
| D06 | CUDA/FP16 | detector | PASS WITH NOTES | cuda_diag; office soak | — |
| D07 | multi-camera process | detector | PARTIAL | two barrier processes; fall single-cam | Gate multi-detector later |
| D08 | multi-pipeline per camera | — | NOT IMPLEMENTED | no scheduler | future gate |
| D09 | restraint/zábrany | detector | PASS WITH NOTES | zahradky_safety apps | DEFERRED visual on-site |
| D10 | file/video analysis | detector | PASS WITH NOTES | development-video; analyze_video.py | Model Lab later |

## C. EVENT PIPELINE

| ID | Funkce | Repo | Stav | Důkaz | Další krok |
|----|--------|------|------|-------|-----------|
| E01 | Event Core persistence | platform | PASS | api health ok; pytest 42 | — |
| E02 | SQLite migrations | platform | PASS | alembic 0001/0002 | — |
| E03 | WebSocket | platform+monitor | PASS WITH NOTES | code WS /ws/v1; monitor useRealtimeEvents | S07 formal test |
| E04 | idempotency | platform | PASS | tests events | — |
| E05 | acknowledgement | platform+monitor | PASS WITH NOTES | BFF+kiosk key; pytest | office S05 without DEV adapter |
| E06 | snapshot | — | NOT IMPLEMENTED | snapshot_url field only | Gate snapshot |
| E07 | incident clip | platform | NOT IMPLEMENTED / PARTIAL design | PR#33 recording ≠ clip worker | Gate clip |
| E08 | test events | platform+monitor | PASS WITH NOTES | include_test_events; DEV adapter | prefer real ingest |
| E09 | recovery after restart | platform | PARTIAL | SQLite persists; S11 not fully re-run this audit | S11 |

## D. MONITOR

| ID | Funkce | Stav | Důkaz | Další krok |
|----|--------|------|-------|-----------|
| M01 | AppShell + nav | PASS WITH NOTES | verify:gate1-shell; shot | — |
| M02 | /live multi-cam | PASS WITH NOTES | PR#13; gate2 shots LIVE | soak; CORS |
| M03 | /alarms | PASS WITH NOTES | code+provider | — |
| M04 | /events | PASS WITH NOTES | real list; media placeholders | snapshots |
| M05 | /cameras | PARTIAL | static CAMERA_REGISTRY; stale copy | bind layout API |
| M06 | /stats | PARTIAL | thin counts | reports later |
| M07 | /system | PASS WITH NOTES | health cards | — |
| M08 | global alarm | PASS WITH NOTES | provider in __root__ | real EC dual alarm |
| M09 | audio | PASS WITH NOTES | WebAudio gated | browser policy |
| M10 | date filters | NOT IMPLEMENTED | events filters limited | Gate history |
| M11 | snapshots/clips UI | NOT IMPLEMENTED | placeholder text | after media |
| M12 | dashboard→/live | PASS | code redirect | — |

## E. CONTROL CENTER (tip PR #34 unless noted)

| ID | Funkce | Stav | Důkaz | Další krok |
|----|--------|------|-------|-----------|
| C01 | START/STOP ALL | PARTIAL | MainWindow.xaml + VM on #34; not on #35 tip | run S01 on #34 publish |
| C02 | camera selection office | PARTIAL | OfficeCameraBootstrap #34 | verify published binary |
| C03 | detector profile PyAV | PARTIAL | DetectorLaunchBuilder #32–34 | align detector #7 |
| C04 | OPEN MONITOR / preview | PARTIAL | XAML actions | runtime click matrix incomplete this freeze |
| C05 | recording mgmt | PARTIAL | #33/#34 scripts+UI | not on #35 |
| C06 | test alarm/fall | PARTIAL | scenarios tab | S05 |
| C07 | hardware/relay | PASS WITH NOTES | USB-4761 tab; TEST MODE | on-site caution |
| C08 | Video Lab | PARTIAL | advanced tab exists | MOVE TO ADVANCED |
| C09 | config editors | PARTIAL | cameras/detectors JSON | ownership docs |

## F. OPERATIONS

| ID | Funkce | Stav | Důkaz |
|----|--------|------|-------|
| O01 | Windows MediaMTX service | PASS WITH NOTES | manage_mediamtx_service.ps1 |
| O02 | Chrome kiosk scripts | PARTIAL | manage_operator_kiosk on #32+ |
| O03 | Event Core start | PASS WITH NOTES | start_internal_event_core.ps1; health ok |
| O04 | disk/cache | PARTIAL | MTX deleteAfter |
| O05 | backup/rollback | PARTIAL | bak files unsafe |
| O06 | on-site commissioning | DEFERRED ON-SITE | checklist on feature branches |

## G. MODEL LAB

| ID | Funkce | Stav |
|----|--------|------|
| L01–L08 | MP4 lab, FileFrameReader reuse, compare, export | NOT IMPLEMENTED (planned only; offline analyze_video is precursor) |
