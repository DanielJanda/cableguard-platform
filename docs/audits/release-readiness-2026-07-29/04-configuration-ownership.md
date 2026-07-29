# 04 — Configuration ownership

## Canonical owners

| Hodnota | Kanonický owner | Duplicitní kopie | Riziko driftu | Návrh |
|---------|-----------------|------------------|---------------|-------|
| camera_id / display_name | platform `runtime/config/cameras.json` (CC) | monitor `CAMERA_REGISTRY`; layout fixture JSON | **HIGH** | Monitor read-only via layout API only |
| mediamtx_path | platform streams.json + mediamtx.local.yml | detector YAML paths; monitor fixture | HIGH | single registry field |
| WHEP base URL | deploy/env (`VITE_WHEP_BASE_URL`) | hardcoded LAN in some scripts | MED | env only |
| RTSP credentials | Windows CredMan / mediamtx.local.yml | **bak files**; never monitor | **CRITICAL** if bak committed | gitignore bak; never track |
| detector profile / backend | CC detectors.json + detector site YAML | launch builder defaults | HIGH | explicit profile in UI |
| Event Core keys | platform `.env` (gitignored) | process env injection in CC #34 | MED | soft-fail if missing |
| monitor layout | platform `GET /monitor/layouts` + tracked JSON fixture | monitor `officeLabLayout.ts` fallback | MED (ok for lab) | no prod registry in monitor |
| recording policy | mediamtx path `record*` | layout `recording_state` UI enum only | MED | future recording-policies.yml |
| ROI | detector YAML / CC roi/*.json | barrier hardcoded polygons | HIGH | one owner per pipeline |
| test vs production | `is_test` / payload test_mode; station ids | enableTestLab compile flag | MED | hard UI labels |

## Separation checks

| Check | Result |
|-------|--------|
| Platform owns registry | **PASS WITH NOTES** (runtime gitignored; examples tracked) |
| Monitor long-lived prod registry | **FAIL tendency** — `CAMERA_REGISTRY` still used on `/cameras` |
| Credentials in tracked config | **PASS** for tracked; **FAIL risk** untracked bak not ignored |
| Office vs production profiles | **PARTIAL** — office YAML + CC bootstrap on #34; easy to mis-select |
| TEST cannot drive prod relay | **PARTIAL** — depends on operator discipline + TEST MODE; not cryptographic |
| Zahrádky not recorded during office test | **PARTIAL** — path-level record flags in local yml; must verify S08/S14 |
| OpenCV silent fallback | **PARTIAL** — profiles exist; UI must show backend |

## Environment variables (high level)

- Detector: `CABLEGUARD_*`, `RTSP_URL_*`, `APP_DEV_VIDEO_PATH`, ingest key  
- Platform: `CABLEGUARD_INGEST_API_KEY`, `CABLEGUARD_KIOSK_API_KEY`, DB path  
- Monitor: `VITE_*` (no secrets); BFF kiosk key server-side only  
