# 07 — Monitor functional & UX audit

**Tip:** PR #13 `f9b4af8`.

## Routes

| Route | Direct URL | Refresh | Back/Fwd | Empty/Load/Error | Stav |
|-------|------------|---------|----------|------------------|------|
| `/live` | yes | yes | expected | loading→grid; API timeout→fixture | PASS WITH NOTES |
| `/alarms` | yes | yes | yes | empty list ok | PASS WITH NOTES |
| `/events` | yes | yes | yes | mock/real | PASS WITH NOTES |
| `/cameras` | yes | yes | yes | static table | PARTIAL |
| `/stats` | yes | yes | yes | thin | PARTIAL |
| `/system` | yes | yes | yes | health | PASS WITH NOTES |
| `/` `/dashboard` | redirect `/live` | — | — | — | PASS |
| kiosks / test-lab | yes | yes | — | full | PASS WITH NOTES |

## GlobalAlarmProvider

| Check | Result |
|-------|--------|
| Lives in `__root__` | PASS (`code`) |
| Survives AppShell nav | PASS WITH NOTES (design+Gate1 tests; S04 runtime sample) |
| Banner on shell pages | PASS |
| Sound survives nav | PASS WITH NOTES (provider-level) |
| Per-event ACK | PASS WITH NOTES (DEV dual + Event Core path) |
| Jump to camera | PASS WITH NOTES (`/live?camera=`) |
| Alarm update remounts WHEP | designed not to (`key=camera_id`; streamName stable) — PASS WITH NOTES |
| Extra WS on alarm | should not — UNKNOWN formal proof |

## Operator hierarchy recommendation (not implementing Gate 2 changes)

1. Live picture  
2. Active alarm (banner + tile ring)  
3. ACK (large)  
4. Jump to camera  
5. Event lookup  
6. Simple system state  

Hide from primary ops: PTS, decoder threading, session UUID, filesystem paths, traceback.

| Item | Directive |
|------|-----------|
| Collapsible sidebar + Escape | KEEP |
| WHEP tech overlay on every tile | MOVE TO ADVANCED / toggle |
| DEV dual alarm button | KEEP in test lab only; never prod build |
| `/cameras` Gate1 copy | RENAME/UPDATE — stale |
| Compact alarm banner | KEEP |
| Multi-alarm rings | KEEP |

## Layout readiness (1–4 cameras)

Shell + `LiveCameraGrid` ready on PR #13. Acceptance soak **pending**. CORS/`webrtcAllowOrigin` must match Monitor origin.

## Verdict

**Monitor operator shell: PASS WITH NOTES.** Multi-cam live: **PASS WITH NOTES** (evidence screenshots). Not production-signed-off until soak + real Event Core dual-alarm + layout API from merged platform tip.
