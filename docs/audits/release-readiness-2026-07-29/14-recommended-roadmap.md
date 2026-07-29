# 14 — Recommended roadmap after audit

Order driven by findings (not legacy roadmap alone).

1. **P0** MediaMTX bak gitignore + quarantine (immediate).  
2. **P1** Platform stack: merge/rebase **#34 → main**, then restack **#35**.  
3. **P1** Detector: make **#7** canonical; close or rebase **#5** into it; keep golden master.  
4. **P1** Office **alarm vertical cut** with real Event Core (no DEV adapter as proof).  
5. Control Center Overview simplify (technician path) on top of #34.  
6. Recording health truthfulness (segments exist ⇔ UI RECORDING).  
7. Monitor Playwright smoke + 30‑min 3-cam soak.  
8. Snapshot pipeline.  
9. Event history filters (UTC/Prague).  
10. Incident clip worker.  
11. Multi-camera Gate 2 polish (already largely on #13 — soak/sign-off).  
12. Multi-detector runtime.  
13. Model Lab.  
14. Production hardening / on-site Zahrádky.

## Direct answers

| Question | Answer |
|----------|--------|
| Continue multi-camera Gate 2 now? | **Not as next coding gate.** Sign-off soak OK later; first fix P0/P1 stack+secrets+alarm cut. |
| Alarm vertical cut first? | **Yes.** |
| Safe to merge now? | **No PR** recommended for immediate merge. |
| Keep draft? | Detector #5–#7; Platform #32–#35; Monitor #13; this audit PR. |
| Close superseded? | Consider close #32/#33 after #34 lands; close #5 if absorbed by #7; review #26 separately. |

## Next recommended gate name

**Gate R1 — Release hygiene & alarm vertical cut**  
(not Model Lab, not multi-detector, not clip worker)
