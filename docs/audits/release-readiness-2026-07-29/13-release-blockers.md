# 13 — Release blockers

| ID | Sev | Nález | Evidence | Dopad | Řešení | Repo | PR návrh | Size | Deps |
|----|-----|-------|----------|-------|--------|------|----------|------|------|
| B01 | P0 | `mediamtx.local.yml.bak*` not gitignored | `git check-ignore` fails; untracked present | credential leak on `git add` | ignore + quarantine files; rotate if leaked | platform | chore/security-mediamtx-bak-ignore | S | none |
| B02 | P1 | PR #35 missing #34 stack | merge-base | wrong runtime tip | restack #35 onto #34 or merge #34 first | platform | stack fix | M | team |
| B03 | P1 | Detector main ≠ PyAV tip | main `a355599` vs #7 `47366e2` | CC launches mismatch | land #7 (reconcile #5) | detector | #7 | L | CC #34 |
| B04 | P1 | Empty CI checks on drafts | gh statusCheckRollup [] | blind merges | enable GHA or document local gate | all | ci | M | — |
| B05 | P1 | Alarm vertical cut not re-proven with real EC ingest this freeze | S05 PARTIAL | false confidence from DEV adapter | office S05 with ingest key | monitor+platform | test runbook | S | Event Core up |
| B06 | P1 | Monitor E2E mostly static | verify:* | UI regressions slip | Playwright smoke | monitor | test | M | — |
| B07 | P2 | CAMERA_REGISTRY drift | cameras.tsx | wrong operator labels | consume layout API | monitor | follow Gate2 | M | #35 |
| B08 | P2 | False READY classes | matrix §09 | silent failure | health truthfulness pass | CC+monitor | feat | L | — |
| B09 | P2 | Leftover WebRTC sessions | MTX readers | resource leak | kiosk lifecycle + session GC | monitor/ops | ops | M | — |
| B10 | P2 | Config absolute path office63_direct | code | brittle | profile-only | detector | chore | S | — |
| B11 | P3 | Doc CURRENT_STATE stale | docs | wrong planning | update after merges | platform | docs | M | merges |
| B12 | P3 | CC Overview overload | UX §06 | slow response | Advanced split | platform | UX | L | #34 |
| B13 | P4 | Stale Gate2 copy on /cameras | UI string | confusion | fix string | monitor | chore | S | — |

## Capacity (no new benchmark)

| Workload | Measured | Estimate | Need before prod |
|----------|----------|----------|------------------|
| 1×720p WHEP | yes (office) | — | — |
| 3×720p WHEP | evidence sample ~minutes | GPU decode assumed | **30min soak** |
| 1× PyAV detect | office soak docs | — | align tip |
| 2–3 pipelines | — | unknown | benchmark on RTX host — **not proven** |
| Recording/camera | office segments on disk | — | disk budget |
| Incident storage | not impl | — | design |

Do **not** treat RTX 3080 headroom as proven.
