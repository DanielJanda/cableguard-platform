# On-site commissioning checklist — Zahrádky horní (post-merge)

**Status:** DEFERRED — first physical visit to the station  
**Not a software merge blocker.** Software integration accepted on office `.63` visual + automated MediaMTX/PyAV soak.

## Purpose

Confirm glass-to-glass / operator-visible realtime parity on the production camera once an operator is on site. Remote comparison of real motion vs monitor is not objective.

## Prerequisites

- Detector PR #7 + Platform PR #32 merged (or running on acceptance tip)
- Production input: `pyav_rtsp` + `source_mode=mediamtx` → `zahradky-horni-stanice`
- MediaMTX path READY, WHEP reachable, Control Center START ALL healthy

## Steps

1. Open WHEP operator view for `zahradky-horni-stanice`.
2. Open lightweight PyAV annotated diagnostics (Control Center debug / `--debug-render lightweight`).
3. Perform a quick person movement in the ROI.
4. Confirm annotation appears realtime vs WHEP (no ~1 s lag).
5. Confirm BotSORT track IDs remain stable during continuous presence.
6. Confirm ROI overlay matches the physical hazard zone.
7. Run a controlled fall test per the safe test scenario (no unsafe staging).
8. Confirm Event Core receives the fall event.
9. Confirm no growing visual lag over ≥10 minutes of observation.
10. Record **PASS / FAIL**, date, and person performing the test below.

## Record

| Field | Value |
|---|---|
| Date | |
| Person | |
| Result (PASS/FAIL) | |
| Notes | |

## Related

- ADR-010 — PyAV low-latency RTSP ingest (ACCEPTED FOR SOFTWARE INTEGRATION)
- Detector audit `docs/audits/project-audit-after-pyav-2026-07-29.md` POST-AUDIT UPDATE
