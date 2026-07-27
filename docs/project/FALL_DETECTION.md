# FALL_DETECTION — algoritmus pádové detekce

Last verified: 2026-07-27

Verified against:

- cableguard-detector `main` commit `c628a2f` — **fall detektor NENÍ na main**
- pracovní větev `feature/mediamtx-input-profile` (draft PR #2), algoritmus shodný s `feature/zahradky-fall-detection` (draft PR #1)

Tento dokument algoritmus **popisuje, nepřepisuje**. Numerická logika je zmrazená golden-master testy.

---

## Umístění

- Algoritmus: `src/cableguard/detection/pad/` (`risk_score.py`, `movement.py`, `roi.py`, `state_machine.py`, `tracking.py`, `pose_inference.py`, `input_profile.py`)
- Entrypoint: `apps/zahradky_horni_pad.py` (`--mode`, `--source`, `--input-profile direct_camera|mediamtx_proxy`, `--input-config`, `--max-frames`, `--no-window`, `--debug-overlay`)
- Site konfigurace: `sites/zahradky/horni_pad.yaml`

## Aktuální parametry (podle kódu — `risk_score.py` + `horni_pad.yaml`, hodnoty identické)

| Parametr | Hodnota | Význam |
|---|---|---|
| `angle_threshold` (ANGLE) | `60` | úhel nohou vůči vertikále |
| `torso_fall_threshold` (TORSO) | `20` | pokles trupu |
| `movement_threshold` (MOV) | `10` | pohyb dolů (hip Y delta) |
| `fall_axis_threshold` (AXIS) | `45` | náklon hlavní osy těla |
| `box_ratio_threshold` (BOX) | `1.0` | poměr šířka/výška bounding boxu |
| `min_keypoint_dist` | `20` | minimální vzdálenost keypointů pro validní výpočet |

**Váhy podmínek (weights):**

| Podmínka | Váha |
|---|---|
| `cond_leg` | `0.15` |
| `cond_torso` | `0.15` |
| `cond_move` | `0.10` |
| `cond_axis` | `0.40` |
| `cond_box` | `0.10` |

**Risk threshold:** `risk_score_threshold = 0.60`

**Fall gate:** `risk_score >= 0.60` **a zároveň** `movement_y > 0` (pohyb směrem dolů).

## Pipeline

- **YOLO pose model:** `models/shared/yolo11m-pose.pt` (Git LFS), logické jméno `zahradky-shared-yolo11m-pose`; inference `imgsz=480`, `classes=[0]` (osoby); CUDA → `device=0, half=True`, jinak CPU fallback
- **SHA256 modelu** (potvrzen v `models/models-manifest.json`): `29B17EAF3A3117CBEA906090DBEDF9159F7C6A49DB58EC8B99ED2DFDE1CF6EB2`
- **BotSORT tracking:** `model.track(persist=True, tracker="botsort.yaml")`; persistentní track ID mezi framy
- **ROI:** polygon `roi_points` v `horni_pad.yaml`; osoba se vyhodnocuje, pokud má **alespoň jeden keypoint** uvnitř polygonu
- **Movement history:** deque `history_maxlen: 5` na track; `movement_y = hip_y − last_hist_y`
- **State machine** (`FallAlertStateMachine`): per-track stav `fall_detected`
- **Duplicate alarm prevention:** flag `fall_alert_sent` — jedna emise na track, dokud `fall_detected` trvá
- **Reset:** jakmile `fall_detected` pomine → `fall_alert_sent = False` (nový pád téhož tracku znovu alarmuje)

## Golden-master ochrana

- Data: `tests/fall_detection/golden/keypoint_sequences.jsonl` + `expected_results.jsonl` (vygenerováno z legacy implementace skriptem `scripts/development/generate_fall_golden_from_legacy.py`)
- Test: `tests/fall_detection/test_golden_algorithm.py` — porovnává per-frame podmínky, úhly, movement, box_ratio, risk score, `fall_risk`, `event_emitted`; tolerance `1e-9` (risk) / `1e-4` (geometrie)
- Spuštění:

```powershell
cd C:\Users\mega\Documents\cableguard-detector
$env:PYTHONPATH = "$PWD\src"
.\.venv\Scripts\python.exe -m pytest tests/fall_detection/ -q
```

- Aktuální výsledek (2026-07-27): **46 passed** (fall_detection + common, 1 integration deselected)

**Pravidlo:** jakákoli změna parametrů či vzorců vyžaduje vědomou regeneraci golden datasetu — jinak testy selžou. To je záměr.

## Algorithm vs. Runtime

### Algorithm (zmrazeno)

Čistá numerická logika: geometrie keypointů, podmínky, váhy, risk score, state machine. Deterministická, testovaná golden masterem, bez závislosti na kameře/GPU.

### Runtime (může se vyvíjet)

Kamera/RTSP připojení, frame pipeline, volba GPU/CPU, reconnect, input profily (`direct_camera` přímo z kamery vs. `mediamtx_proxy` z MediaMTX `:8554`; env `CABLEGUARD_MEDIAMTX_RTSP_URL`, `CABLEGUARD_FALL_INPUT_PROFILE`), publishery. Změny runtime nesmí měnit výstupy golden testů.

## Známé parity rozdíly vs. legacy (`docs/audits/fall-algorithm-parity.md` v detector repu)

1. **Frame pipeline:** legacy drží vždy nejnovější frame a pod zátěží starší zahazuje; nová app po zpracování frame holder čistí a čeká na další. Na živém RTSP to znamená **jiné vyhodnocované framy / jiné FPS chování** — numerika je stejná, výběr framů ne. Video-level parita neověřena (golden master je čistě numerický).
2. **Device/half:** nová app má CPU fallback; legacy předpokládá výhradně GPU.
3. **Výjimky z `track()`:** legacy pokračuje dál; nová app může proces shodit — chybí wrap/restart (patří do Phase 3/6 runtime hardening).

## Stav integrace s Event Core

- Na pracovní větvi: `EventCorePublisher` je **stub** (`NotImplementedError`); `horni_pad.yaml` má `publishers.event_core.enabled: false`.
- Plná implementace (HTTP POST `/api/v1/events`, outbox, retry, heartbeaty) žije na větvi **`feature/fall-event-core-integration`** — nemergováno, cíl Phase 4.
- Aktivní publishery dnes: **JSONL** (`runtime/.../fall_events.jsonl`), volitelně **Telegram** (env).
