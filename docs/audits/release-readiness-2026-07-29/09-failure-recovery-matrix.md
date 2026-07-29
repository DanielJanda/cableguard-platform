# 09 — Failure and recovery matrix

| Komponenta | Výpadek | Detekce | Dopad | Auto obnova | UI stav | Data loss | Test |
|------------|---------|---------|-------|-------------|---------|-----------|------|
| Camera | RTSP down | MTX ready=false; PyAV errors | no frames | reconnect | STALE/OFFLINE | none | S09 PARTIAL |
| MediaMTX | process kill | status scripts; WHEP fail | all video+record | service restart | all tiles ERROR | recording gap | S10 PARTIAL |
| PyAV reader | decode fail | logs; freshness | detector blind | reconnect smoke | CC health | miss events | smoke tool |
| Detector | crash | no heartbeat | no alarms | CC restart | STOPPED | miss | S03 |
| Event Core | down | /health; WS closed | no new events | restart script | EC footer | if disk ok history kept | S11 |
| SQLite | corrupt | API 500 | history/ack break | restore backup | errors | **yes** | UNKNOWN |
| Monitor | tab crash | — | UI gone | reload | — | none | S13 |
| Control Center | crash | — | lose ops UI | relaunch | — | none | — |
| Recording | disk full | MTX errors | no segments | cleanup policy | misleading RECORDING | **yes** | S08 |
| Playback | port closed | fetch fail | no scrub | restart MTX | — | none | — |
| Disk | full | OS | record/db fail | alert needed | may still show READY | **yes** | P2 gap |
| Audio | autoplay block | silent | missed alarm sound | user Arm sound | armed flag | none | S05 |
| Network | LAN blip | WS/WHEP | transient | backoff | reconnecting | possible miss | S07 UNKNOWN |

## False READY warnings

| Appearance | Reality |
|------------|---------|
| Detector process running | no frames / stale slot |
| Tile LIVE badge | frozen last frame if STALE logic bypassed |
| recording_state RECORDING in layout | no new mp4 segments |
| WS connected | events not persisting |
| Audio enabled in UI | browser not primed |
| CC service green | wrong profile / OpenCV fallback unintended |
| Platform tip #35 | CC ops from #34 missing |
