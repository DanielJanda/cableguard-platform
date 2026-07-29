# 08 — E2E scenarios (office TEST)

Environment: office camera / synthetic MediaMTX paths. Zahrádky visual = **DEFERRED ON-SITE**.

| ID | Scénář | Stav | Skutečný výsledek / poznámka | Důkaz |
|----|--------|------|-----------------------------|-------|
| S01 | Cold START ALL | PARTIAL / UNKNOWN | Not fully re-run this freeze; scripts exist on #34 | code |
| S02 | Office detection PyAV | PASS WITH NOTES | Prior office acceptance on detector #7 tip | detector audits |
| S03 | Stop/restart detector | PARTIAL | Code paths in CC #34; not re-clicked | code |
| S04 | Monitor nav live→events→system→live | PASS WITH NOTES | Provider in root; routes exist; prior Gate1 | verify:gate1; runtime /live 200 |
| S05 | Test alarm EC→WS→UI→ACK | PARTIAL | DEV adapter proven on /live; real ingest not re-proven today | shot alarms-dual |
| S06 | Refresh with open alarm | PARTIAL | Designed via listEvents; not re-run | code |
| S07 | WS reconnect | UNKNOWN | not exercised this freeze | — |
| S08 | Recording office / Zahrádky off | PARTIAL | local recordings dir; feature on #33/#34 | runtime/recordings; PR docs |
| S09 | One camera offline | PASS WITH NOTES | MTX delete grid-2 isolated; UI isolation designed | gate2 RESULTS |
| S10 | MediaMTX restart | PARTIAL | reconnect code exists; not re-run | code/smoke |
| S11 | Event Core restart | PARTIAL | health ok now; restart not re-run | api health |
| S12 | STOP ALL | UNKNOWN | not re-run | — |
| S13 | Browser/kiosk restart | PARTIAL | leftover WebRTC readers observed historically | MTX readers count |
| S14 | Test vs production | PASS WITH NOTES | is_test filtering tests; UI TEST badges | pytest test_test_events; UI |
| S15 | Snapshot | NOT IMPLEMENTED | — | — |
| S16 | Incident clip | NOT IMPLEMENTED | rolling ≠ clip job | — |
| S17 | Date filters | NOT IMPLEMENTED | — | — |
| S18 | Multi-camera | PASS WITH NOTES | Gate2 on monitor #13 with LIVE shots | screenshots |
| S19 | Multiple alarms | PASS WITH NOTES | DEV dual; per-ack | alarms-dual / alarm-single |
| S20 | Model Lab | NOT IMPLEMENTED | — | — |

## Limits

This freeze prioritized inventory + safe probes over full S01–S12 marathon. Treat PARTIAL/UNKNOWN as **must re-run** before production sign-off.
