# USB-4761 relay mapping audit — 2026-07-28

## HISTORICAL MAPPING (documented in source / git)

| Channel (1-based) | Function | Polarity | Mode |
|-------------------|----------|----------|------|
| 1 | Green / OK | active-high (`WriteBit=1` = ON) | steady while OK |
| 2 | Red / warning | active-high | danger with ch3 |
| 3 | Buzzer / siren | active-high | danger with ch2 |
| 4–8 | unused in production semaphore | — | — |

### Evidence (commits / files)

| Repo | Path | Evidence |
|------|------|----------|
| cableguard-detector | `safety-invariants.md` | „Kanál 1 = zelená (OK), kanál 2 = červená, kanál 3 = siréna. Mapování se nemění.“ |
| cableguard-detector | `docs/usb4761.md` | Same table; imported baseline |
| cableguard-detector | `relay_server.py` | `CHANNEL_OK = 1`, `CHANNELS_DANGER = (2, 3)` |
| cableguard-detector | `advantech_relay.py` | `WriteBit(port, bit, 1 if on else 0)`; `set_relay(1..8)` |
| cableguard-detector | commit `9031653` | Import working Zahradky detector baseline (docs + relay) |
| cableguard-platform | `tools/control-center` hardware docs | Channels documented as 1-based semantic mapping |

**DI inputs:** historical production code focuses on DO relays for semaphore/siren. No authoritative DI→function map found for office Admin Studio (barrier scripts may use DI elsewhere — treat DI as read-only telemetry until confirmed).

**Active level:** ON = logic 1 written to DO bit (active-high relative to BDaq WriteBit).

## PHYSICALLY CONFIRMED MAPPING

**Status: NOT CONFIRMED in this session.**

No live wiring walk-through (green lamp / red lamp / buzzer audible) was performed.
Therefore Admin Studio keeps:

```json
"mapping_physically_confirmed": false
```

Semantic Green/Red/Buzzer buttons remain **disabled** even if historical channels are filled in `hardware.json`.

## Local gitignored config (historical only)

`runtime/config/hardware.json` may contain:

```json
{
  "version": 1,
  "device_description": "USB-4761,BID#0",
  "green_channel": 1,
  "red_channel": 2,
  "buzzer_channel": 3,
  "mapping_physically_confirmed": false,
  "mapping_source": "HISTORICAL: detector safety-invariants.md + relay_server.py CHANNEL_OK/CHANNELS_DANGER"
}
```

Do **not** set `mapping_physically_confirmed=true` until a supervised live check of ch1/ch2/ch3 against the physical semaphore/buzzer.

## Guarded write policy

Pulse allowed only when:

1. Native adapter `CONNECTED` (BDaq4 open + DI/DO read)
2. HARDWARE TEST MODE ON
3. For semantic buttons: `mapping_physically_confirmed=true`
4. Channel pulse (Relé 1–3): TEST MODE + CONNECTED (operator confirms channel intent) — still no detector→relay auto

Max pulse: **250 ms**, then ALL OFF + read-back.
