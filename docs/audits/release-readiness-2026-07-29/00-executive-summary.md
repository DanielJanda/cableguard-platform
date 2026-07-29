# CableGuard release-readiness audit — executive summary

**Audit date:** 2026-07-29  
**Mode:** FEATURE FREEZE (documentation only; no product feature work)  
**Verdict:** **NOT READY — FIX P0/P1 FIRST**

## Scope HEADs (verified locally + GitHub)

| Repo | Checkout / tip used for audit | SHA |
|------|-------------------------------|-----|
| cableguard-detector | detached = PR #7 tip `spike/pyav-low-latency-office63` | `47366e2` |
| cableguard-platform | docs branch from `origin/main`; open feature tips also reviewed | `main` `e07adc0`; PR35 `b4988de`; PR34 `afed13e` |
| cableguard-monitor | `feat/operator-navigation-multiview` (PR #13) | `f9b4af8` |

## Counts (feature catalog, approximate)

| Status | Count |
|--------|------:|
| PASS / PASS WITH NOTES | 28 |
| PARTIAL | 24 |
| FAIL | 3 |
| NOT IMPLEMENTED | 18 |
| DEFERRED ON-SITE | 6 |
| OBSOLETE / UNKNOWN | 5 |

Exact IDs: see `02-feature-catalog.md` and `audit-summary.json`.

## P0 / P1 blockers (must fix before next feature gate)

1. **P0 — MediaMTX backup configs with credentials are untracked and not gitignored**  
   `deploy/mediamtx/mediamtx.local.yml.bak*` is **not** matched by `*.local.yml`. Accidental `git add .` would leak RTSP credentials.  
   *Proposed fix:* ignore `*.bak`, `*.yml.bak*`; delete local bak or move outside repo; rotate camera passwords if ever committed historically.

2. **P1 — Platform PR stacking / checkout confusion**  
   PR **#32 ⊂ #33 ⊂ #34** (stacked). PR **#35** branches from **main** and does **not** contain #34. Working tree often on #35 ⇒ Control Center PyAV/ops UX and recording helpers are **absent** from that tip.  
   *Risk:* operators believe “platform is ready” while running incomplete tip.

3. **P1 — Detector checkout is detached on spike tip; `main` lacks PyAV production path**  
   Production-intended PyAV lives on draft PR #7; `main` is still older OpenCV/latest-frame lineage. Control Center on #34 expects detector PyAV profiles that may not match checked-out detector `main`.

4. **P1 — No green CI status checks** on open draft PRs (empty `statusCheckRollup`). Merge readiness is local-only.

5. **P1 — Monitor verifies are mostly static source greps**, not browser E2E against Event Core. Gate 2 30‑min soak **pending**.

## Top 5 operational risks

1. Secret footgun (`*.bak` MediaMTX configs).  
2. Wrong platform branch running → START DETECTION / recording / layout API mismatch.  
3. False READY: process up but no frames / recording ON without segments / WS up without persistence.  
4. Leftover WebRTC sessions on MediaMTX (office path showed many readers during audit probes).  
5. TEST vs PRODUCTION separation depends on env + `include_test_events`; CC hardware tab can still drive USB-4761 in TEST MODE if misused on-site.

## UX verdicts

| Surface | Verdict |
|---------|---------|
| **Control Center** | Powerful but overloaded: ops + video lab + hardware + ROI on one window. Needs role split (technician / admin / developer). Critical office workflow exists on PR #34 tip only — **PARTIAL**. |
| **Monitor** | Gate 1 shell **PASS WITH NOTES**; Gate 2 multi-cam on PR #13 **PASS WITH NOTES** (fixture fallback, CORS origin sensitivity, soak pending). Operator hierarchy is better than CC; WHEP diagnostic overlays are too technical for primary ops. |

## Recommended next gate (after P0/P1)

**Not** multi-camera polish and **not** Model Lab.

1. Secure MediaMTX bak ignore + credential hygiene.  
2. Reconcile platform PR stack: merge or restack **#34 → main**, then restack **#35** on top (or fold layout API into #34).  
3. Align detector **#7** (or squash of 5/6/7) with CC launcher.  
4. Vertical **office alarm cut**: Event Core test event → WS → Monitor banner → ACK (no DEV adapter as production proof).  
5. Then Gate 2 soak + recording health; only then Model Lab / multi-detector.

## Merge recommendations (summary)

| Action | PRs |
|--------|-----|
| Keep draft | Detector #5,#6,#7; Platform #32–#35; Monitor #13 |
| Do not merge yet | All of the above until stack + secrets + CI clarified |
| Ready-ish after rebase/CI | Platform #34 (contains 32+33) as ops foundation; Monitor #13 after soak; Detector #7 as PyAV canonical |
| Close / supersede candidates | Detector #5 if #7 fully replaces latest-frame; Platform #32/#33 if merged via #34; Platform #26 review separately |
| This audit PR | Draft docs-only — do not merge until team accepts verdict |

Zahrádky visual acceptance remains **DEFERRED ON-SITE**.
