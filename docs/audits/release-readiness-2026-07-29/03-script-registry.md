# 03 — Script and entrypoint registry

Inventory counts (approx, excluding `.venv`/`node_modules`/`runtime`): detector ps1=3 py=74 bat=3; platform ps1=14 py=49; monitor ps1=1.

Nothing deleted or moved (cleanup manifest only).

## PRODUCTION ENTRYPOINT

| Path | Účel | Secrets | Testovaný | Používaný | Doporučení |
|------|------|---------|-----------|-----------|------------|
| detector/`zahradky_safety/app.py` | horní zábrana | RTSP via env | pytest subset | yes prod | KEEP |
| detector/`…/app_relay.py` | dolní zábrana | env | limited | yes | KEEP |
| detector/`relay_server.py` | USB-4761 holder | — | relay tests | yes | KEEP |
| detector/`apps/zahradky_horni_pad.py` | fall detector | Event Core key env | pytest+office | yes (office) | KEEP; require PyAV profile explicit |
| platform/`scripts/start_mediamtx.ps1` | MediaMTX | local yml | manual | yes | KEEP |
| platform/`scripts/manage_mediamtx_service.ps1` | Win service | — | manual | yes | KEEP |
| platform/`scripts/start_internal_event_core.ps1` | Event Core | `.env` | pytest/health | yes | KEEP |
| platform/`scripts/start_internal_cableguard.ps1` | full LAN stack | env | manual | yes | KEEP |
| monitor/`npm run dev:internal-lan` | operator UI | Vite local env | verify:* | yes | KEEP |
| CC/`CableGuard.ControlCenter` | admin GUI | CredMan + env | xUnit on #34 | yes when published | publish from #34 tip |

## RUNTIME ORCHESTRATION

| Path | Účel | Doporučení |
|------|------|------------|
| detector/`Start_obe_kamery.bat` / `start_oba_skripty_s_rele.ps1` | start barriers+relay | KEEP |
| detector/`Stop_kamery.bat` | kill processes | KEEP |
| platform/`scripts/stop_mediamtx.ps1` | stop MTX | KEEP |
| platform PR#32+ `start_production_monitor.ps1` / `manage_operator_kiosk.ps1` | kiosk | KEEP on merge #34 |
| CC `DetectorProcessManager` | start/stop detector | KEEP; verify env injection |

## INSTALLATION / MAINTENANCE

| Path | Doporučení |
|------|------------|
| platform/`setup_mediamtx_local_config.ps1` | KEEP |
| platform/`patch_mediamtx_internal_lan.ps1` | KEEP |
| platform/`ensure_internal_firewall.ps1` | KEEP |
| platform/`reset_development_database.ps1` | KEEP (require confirm) |
| detector/`docs/installation.md` flow | KEEP |

## DIAGNOSTIC / BENCHMARK / TEST HELPER

| Path | Kategorie | Doporučení |
|------|-----------|------------|
| detector/`tools/diagnostics/pyav_reconnect_smoke.py` | DIAGNOSTIC | KEEP |
| detector/`scripts/development/*` | TEST/BENCHMARK | KEEP under development/ |
| detector/`apps/pyav_rtsp_raw_preview.py` | DIAGNOSTIC | KEEP; office-only |
| platform/`scripts/status_mediamtx.ps1` | DIAGNOSTIC | KEEP |
| platform/`scripts/test_mediamtx_stream.ps1` | DIAGNOSTIC | KEEP |
| platform/`scripts/simulate_system.py` | TEST HELPER | KEEP |
| platform/`scripts/enable_office_lab_grid_streams.py` | TEST HELPER | KEEP; label SYNTHETIC |
| platform PR#33 `enable_office_rolling_recording.ps1` / `status_office_recording.ps1` | MAINTENANCE | KEEP with #34 |
| monitor/`scripts/verify-*.mjs` | TEST HELPER | KEEP; label static vs runtime |
| monitor/`verify:internal-lan-whep` | DIAGNOSTIC/runtime | KEEP |

## ONE-OFF / OBSOLETE / UNKNOWN

| Path | Stav | Doporučení |
|------|------|------------|
| Docs-referenced missing scripts (`verify_dual_camera_paths.ps1`, etc.) | OBSOLETE / elsewhere | mark SUPERSEDED in docs |
| Local `mediamtx.local.yml.bak*` | UNKNOWN risk | **gitignore + delete/move** |
| Detector stashes | UNKNOWN WIP | review/drop |

## Monitor package.json scripts

`dev`, `dev:internal-lan`, `build*`, `preview`, `lint`, `format`, `verify:secrets|contracts|office-*|gate1|gate2|whep|internal-lan-whep` — all CURRENT.

## Notes

- OpenCV RTSP still used for barriers and as fall fallback — **intentional**, not obsolete.
- Hardcoded office IP appears in detector office helpers/docs — avoid expanding; prefer profiles.
- Absolute path to platform `mediamtx.local.yml` in detector `office63_direct.py` — **HIGH drift risk**.
