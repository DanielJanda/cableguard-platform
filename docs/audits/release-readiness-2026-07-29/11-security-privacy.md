# 11 — Security and privacy

**No secret values are recorded in this document.**

## Findings

| Typ | Repo/path | Tracked? | Severity | Akce |
|-----|-----------|----------|----------|------|
| MediaMTX config backup likely containing RTSP credentials | platform `deploy/mediamtx/mediamtx.local.yml.bak` | **untracked; NOT gitignored** | **P0** | gitignore `*.bak` / `*bak*`; move/delete; never `git add`; rotate if ever pushed |
| Same | `mediamtx.local.yml.bak-before-recording-gate1` | untracked; not ignored | **P0** | same |
| Live MediaMTX local config | `mediamtx.local.yml` | gitignored via `*.local.yml` | OK | keep |
| Platform `.env` | root | gitignored | OK | keep |
| Monitor `*.local` env | monitor | gitignored | OK | keep |
| Example placeholders | `.env.example`, mediamtx.example.yml | tracked placeholders | OK | keep |
| Test fake credentials | detector/platform tests | tracked fakes | OK | keep |
| Absolute local path to mediamtx.local.yml | detector office63_direct | tracked path string | P2 | remove hardcode |
| Runtime recordings / sqlite | `runtime/`, `data/*.sqlite3*` | gitignored | OK | confirm retention policy (ops/GDPR) |
| Gate2 screenshots office interior | monitor docs/gate2-evidence | tracked TEST imagery | P3 | OK for lab; no creds seen |
| Playback bind | MTX playback localhost | config | OK | keep localhost-only |
| Frontend RTSP creds | monitor sources | forbidden by verify | OK | keep verifies |
| TEST→relay | CC hardware | code | P1 operational | discipline + interlocks |

## Checks

| Check | Result |
|-------|--------|
| runtime media gitignored | PASS |
| playback localhost | PASS WITH NOTES |
| Test event physical output | PARTIAL — policy not hard-enforced |
| Frontend without RTSP secrets | PASS (verifies) |
| snapshot/clip path traversal | N/A until implemented — design requirement |
| prod/test visible separation | PASS WITH NOTES (badges) |

## GDPR / retention

Operational decision **not ratified** in this audit — flag as ops task (recording deleteAfter, incident retention, who may view office camera).
