# Admin Studio / Runtime Integration — Consolidation Audit

**Date:** 2026-07-28  
**Scope:** `cableguard-platform`, `cableguard-detector`, `cableguard-monitor`  
**Mode:** read-only (no code changes, no commits, no push, no merge, no stash, no deletes)  
**Trigger:** Draft PR #24 + substantial local dirty tree after Video Lab / ROI / MediaMTX lifecycle work

---

## 1. Executive summary

The layered model (Video / Detection / Event / Presentation + Admin Studio) is directionally correct. The immediate risk is **scope sprawl**: PR #24 already mixes Video Qualification Lab, Czech UI, docs hub, and detector-preview UX, while **critical local fixes** (MediaMTX stale-PID adoption, ROI camera snapshot, Video Lab UI deadlock, detector OpenCV window size) sit **uncommitted** on the working trees.

**Operational truth today:**

| Concern | Actual source of truth today | Gap vs target |
|---|---|---|
| Code / schemas / defaults | Git | OK |
| Local cameras/streams/ROI/scenarios | `runtime/config/` (gitignored) | OK |
| Process alive? | Control Center (PID file / process name / ports) | Partially fixed locally, not in PR |
| Functional detector health | **Missing** — Admin Studio treats python process ≈ RUNNING | Event Core heartbeat exists but fall `EventCorePublisher` is **not implemented / disabled** |
| Presentation | Monitor via Event Core WS/snapshot | Works for services that heartbeat; fall detector does not yet |

**Verdict for PR #24 merge: NO-GO** until local dirty changes are classified into commits/PRs (or consciously discarded) and the PR description matches what will actually merge.

---

## 2. Repo / branch / PR matrix

| Repo | Current branch | HEAD | `origin/main` | Ahead/behind vs main | Tracks remote branch? | Open PR |
|---|---|---|---|---|---|---|
| **platform** | `feature/video-qualification-lab` | `2aa96ab` | `77e073d` (Admin Studio Phase 2.6) | **4 ahead / 0 behind** | Yes, equal to `origin/feature/video-qualification-lab` for **commits**; **dirty working tree** | Draft [#24](https://github.com/DanielJanda/cableguard-platform/pull/24) → `main` |
| **detector** | `feature/mediamtx-input-profile` | `72e74d0` | `c628a2f` (LFS models) | **21 ahead / 0 behind** | Yes, remote in sync for commits; **1 local mod** | Draft [#2](https://github.com/DanielJanda/cableguard-detector/pull/2) → `feature/zahradky-fall-detection` (not main!) |
| **monitor** | `feature/zahradky-camera-comparison` | `8d8cc0c` | `f085ef0` | **2 ahead / 0 behind** | Yes; dirty docs + untracked WHEP scripts | Draft [#9](https://github.com/DanielJanda/cableguard-monitor/pull/9) |

### Platform PR #24 (already pushed)

Commits on remote:

1. `4fe80e3` — feat: Video Qualification Lab  
2. `ea97a4c` — fix: Video Lab ComboBox stack overflow  
3. `e66ff7f` — fix: Czech UI + detector preview  
4. `2aa96ab` — fix: readable dark controls + `DOKUMENTACE.md` hub  

Stats (GitHub): **+2302 / −748** across control-center + docs.

### Related open PRs (context, not this audit’s merge targets)

- Platform: #5, #6 (deferred), #7, #9, #10 — older LAN/WHEP/camera work  
- Detector: #1 fall baseline (draft), #2 MediaMTX input profile (draft, bases on #1 branch)  
- Monitor: #5 deferred, #6, #8, #9  

### Merge bases

| Repo | merge-base with `origin/main` |
|---|---|
| platform | `77e073d` |
| detector | `c628a2f` |
| monitor | `f085ef0` |

---

## 3. Local dirty files

### platform (`feature/video-qualification-lab`) — **not pushed**

| Path | Kind |
|---|---|
| `scripts/start_mediamtx.ps1` | M |
| `scripts/stop_mediamtx.ps1` | M |
| `tools/control-center/.../AdminLabModels.cs` | M (`roi_role`) |
| `.../Components.cs` | M (MediaMTX live process / port heal) |
| `.../Interfaces.cs` | M (`FindProcessByName`) |
| `.../WindowsProcessInspector.cs` | M |
| `.../StreamFrameGrabber.cs` | **?? untracked** |
| `.../App.xaml.cs` | M |
| `.../MainWindow.xaml` | M |
| `.../MainWindow.xaml.cs` | M |
| `.../LabTabsViewModels.cs` | M (ROI + Ensure MediaMTX) |
| `.../VideoLabViewModel.cs` | M (async RefreshResources) |

Diff scale: **~+655 / −75** across 11 paths (+1 new file).

### detector — **not pushed**

| Path | Kind |
|---|---|
| `apps/zahradky_horni_pad.py` | M (OpenCV `resizeWindow` on first frame) |

### monitor — **not pushed / mostly unrelated**

| Path | Kind |
|---|---|
| `docs/local-integration.md` | M (large rewrite) |
| `scripts/whep-*.mjs` (10 files) + `mediamtx-builtin-player.mjs` + `run-realtime-acceptance.py` | **?? untracked** diagnostics |

### Intentionally absent from Git (verified ignored)

| Path | Rule |
|---|---|
| `runtime/**` (config, logs, mediamtx PID, ROI JSON on disk) | root `.gitignore` → `runtime/` |
| `tools/control-center/publish/**` | `tools/control-center/.gitignore` → `publish/` |
| `deploy/mediamtx/mediamtx.local.yml` | gitignored |

---

## 4. File classification

### A — belongs in PR #24 (same acceptance: Video Lab + Admin Studio usability)

| Item | Reason |
|---|---|
| Already-pushed Video Lab core (`VideoLab*`, assets, thresholds, tests) | Primary PR subject |
| Czech UI / contrast / docs hub (pushed) | Same operator surface |
| **Local** `VideoLabViewModel` async resources fix | Bugfix for Video Lab button that freezes UI — must land before merge |
| **Local** ROI snapshot UI + `StreamFrameGrabber` + Calibration stream/role UX | Operators cannot qualify ROI without it; same Admin Studio acceptance |
| **Local** `roi_role` on `RoiProfile` | Needed for barrier multi-ROI editor story |

### B — prefer **separate platform fix commit** (still OK *inside* #24 if clearly labeled; else small fix PR)

| Item | Reason |
|---|---|
| `scripts/start_mediamtx.ps1` / `stop_mediamtx.ps1` adopt/heal | Lifecycle correctness; affects all starts, not only Video Lab |
| `Components.cs` MediaMTX status via process name + port | Same |
| `WindowsProcessInspector.FindProcessByName` + interface | Same |

**Recommendation:** keep as **dedicated commits at the tip of #24** (`fix(mediamtx): adopt running process / heal stale PID`) rather than a second open PR, unless #24 is frozen for review.

### C — separate detector PR / commit

| Item | Reason |
|---|---|
| `apps/zahradky_horni_pad.py` window resize | Detector repo; UX for `--debug-overlay`; does not belong in platform #24 |
| Existing remote commits on `feature/mediamtx-input-profile` | Already PR #2 |

### D — runtime/local config — never commit

| Item | Reason |
|---|---|
| `runtime/config/{cameras,streams,detectors,notifications,scenarios}.json` | Site-local |
| `runtime/config/roi/*.json` (incl. seeded barrier templates) | Local calibration outputs |
| `runtime/mediamtx/mediamtx.pid`, `*.log` | Process ownership / logs |
| `runtime/test-results/**`, video-lab reports | Generated |

### E — build artefacts — never commit

| Item | Reason |
|---|---|
| `tools/control-center/publish/**` | Local publish output (already ignored) |
| `bin/`, `obj/` | Build |

### F — diagnostic one-offs — do not merge without separate audit

| Item | Reason |
|---|---|
| monitor `scripts/whep-*.mjs`, `run-realtime-acceptance.py`, etc. | Untracked LAN/WHEP experiments; risk of secret leakage / false “supported” tooling |
| Ad-hoc screenshots under publish (if any) | Local verification only |

### G — unclear / needs human decision

| Item | Question |
|---|---|
| monitor `docs/local-integration.md` large local rewrite | Keep on camera-comparison PR #9, move to docs-only PR, or discard? |
| Whether MediaMTX lifecycle commits stay in #24 or split | Reviewer bandwidth vs “one PR to run Admin Studio” |
| Barrier launch from Admin Studio | GUI accepts `detector_type=barrier`, but apply-ROI / Event Core / CLI contract incomplete |

---

## 5. Process lifecycle map

### Authoritative scripts (platform)

| Component | Start | Stop | PID / logs | Ports |
|---|---|---|---|---|
| MediaMTX | `scripts/start_mediamtx.ps1` | `scripts/stop_mediamtx.ps1` | `runtime/mediamtx/mediamtx.pid`, `mediamtx.*.log` | 8554 RTSP, 8889 WebRTC/WHEP, 9997 API, optional 9998 metrics |
| Event Core | `scripts/start_internal_event_core.ps1` | via PID tree in CC | `runtime/event-core/event-core.pid` | 8000 |
| Monitor | internal start script / npm (CC component) | PID file | `runtime/...` | 8080 (prod) / 5173 (dev often leftover) |
| Fall detector | **not** a PS1 — `DetectorProcessManager` → `python apps/zahradky_horni_pad.py ...` | Kill by managed PID / cmdline hint | `runtime/logs/detectors/<id>.*.log` | none (consumes RTSP) |
| Barrier detector | same manager; script typically under `zahradky_safety/` | same | same | none |

Also present: `start_internal_cableguard.ps1`, `start_local_demo.ps1`, `start_backend.ps1` — broader/demo paths; **Admin Studio should not fork a second MediaMTX launcher**.

### Control Center wiring

- `ComponentFactory` → `ServiceComponent` for MediaMTX / Event Core / Monitor / primary Detector  
- MediaMTX start/stop = PowerShell scripts above  
- `StartAllOrchestrator`: skips component if status already `Running`  
- Detectors tab: `DetectorProcessManager` + `DetectorLaunchBuilder`  
- ROI tab (local): `EnsureMediaMtxCommand` → **only** MediaMTX component start + ffmpeg snapshot  

### Duplicate / risk points

1. **Stale PID file** (observed 2026-07-28: pidfile `31816`, live `31500`) → CC thought MediaMTX stopped → start → “port in use”. **Fixed locally**, not in pushed #24.  
2. **Orphan `mediamtx.exe`** without pidfile → same class of failure; local start script now **adopts**.  
3. **Dev leftover processes** (historical `npm run dev` on 5173, long-lived terminals) confuse operators; CC does not own them.  
4. Detector “RUNNING” = process found by PID map or `python*` cmdline hint — **no heartbeat**.  

### Recommended single ownership path

```
Control Center (only operator entry for production-like stack)
  ├─ MediaMTX     → scripts/start|stop_mediamtx.ps1
  ├─ Event Core   → scripts/start_internal_event_core.ps1 (+ stop via PID)
  ├─ Monitor      → managed start/stop scripts
  └─ Detectors    → DetectorProcessManager (python entrypoints)
Manual CLI allowed for debugging, but must write/heal the same PID files.
```

Do **not** add a second WPF-native MediaMTX process host.

---

## 6. Configuration source-of-truth map

```
Git
  platform: schemas, example configs, control-center code, deploy/mediamtx.example.yml
  detector: sites/zahradky/horni_pad.yaml (ACTIVE fall ROI + thresholds + publishers flags)
  detector: zahradky_safety/app.py (ACTIVE barrier ROI constants)
  monitor: UI + Event Core client types

runtime/config/  (gitignored, local)
  cameras.json, streams.json, detectors.json, notifications.json, scenarios.json
  roi/*.json     ← SAVED profiles from Admin Studio (not auto-active)

Event Core DB / WS
  ServiceHealth from POST /api/v1/heartbeats
  System status snapshot for Monitor

Control Center
  Reads runtime/config; starts/stops; local Video Lab samples in memory / runtime/test-results
  Does NOT currently push detector functional status to Event Core

Monitor
  Read-only consumer of Event Core (+ WHEP video)
```

**Violations / soft violations today**

- Admin Studio detector status ≠ Event Core status (parallel truth).  
- SAVED ROI in `runtime/config/roi` is **not** what fall/barrier load.  
- Video Lab G2G sample keyed primarily by `streamId` (see §8).  
- Hardware / Telegram “test” paths partially placeholder.

---

## 7. ROI workflow

### What exists

| Role | Location | Loaded by runtime? |
|---|---|---|
| Fall ACTIVE ROI | `cableguard-detector/sites/zahradky/horni_pad.yaml` → `roi_points` | **Yes** (detector YAML) |
| Barrier ACTIVE ROI | `zahradky_safety/app.py` `ROI_PERSON` / `ROI_SAFETY_BAR` / `ROI_EXCLUDE_PERSON` | **Yes** (hardcoded) |
| SAVED ROI profiles | `runtime/config/roi/*.json` via `RoiProfileService` | **No** — editor only |
| Admin Studio model | `RoiProfile` + local `roi_role` (`fall` \| `person` \| `safety_bar` \| `exclude`) | storage metadata |

### Current editor behaviour (local dirty)

1. Select stream  
2. Optionally **Spustit MediaMTX + načíst snímek** (does **not** start Event Core / Monitor / detector)  
3. ffmpeg grab `rtsp://127.0.0.1:8554/<path>` → JPEG background  
4. Click polygon in **native frame pixel space**  
5. Save JSON under `runtime/config/roi/`  
6. Dialog explicitly says barrier points must be **copied** into `app.py` / YAML  

### SAVED vs ACTIVE (critical)

```
SAVED  = runtime/config/roi/<id>.json
ACTIVE = detector process config (YAML / app.py constants) at last successful load/restart
```

GUI **must not** claim SAVED == ACTIVE until heartbeat (or at least restart + config hash) confirms. **Today there is no apply/restart/hash confirmation path.**

### Resolution risk

Barrier templates use coordinates up to ~1821×1016; fall YAML uses another space (~1171×556). Snapshot uses whatever MediaMTX currently delivers. **No resolution fingerprint on ROI JSON yet** → high risk of silent mis-draws after camera profile change.

**Proposed guard (design only):** store `frame_width`/`frame_height`/`stream_id`/`captured_at` on save; on load compare to current grab; warn if mismatch.

### Algorithm constraint

Fall thresholds / golden master / frame drop policy: **out of scope for these GUI fixes** — audit confirms editor does not write algorithm code.

---

## 8. Latency measurement workflow

### Rename (proposal for next UX pass)

| Current | Target |
|---|---|
| Informal “kalibrace latence” | **Ruční měření glass-to-glass latence** |
| UI section today | “Test latence (ruční)” + `Otevřít vzor času` |

Nothing is calibrated; value is **operator-entered**.

### Current implementation strengths

- Default banner: `GLASS-TO-GLASS LATENCY: NOT MEASURED`  
- Manual record only; automated path marked **EXPERIMENTAL** / non-authoritative  
- Explicit separation copy: transport ≠ G2G ≠ detector freshness; LIVE ≠ REALTIME  
- Pattern asset: `tools/control-center/assets/latency-pattern.html`  
- Qualification can stay `INCOMPLETE` without G2G  

### Gaps vs target wizard + fingerprint

| Need | Status |
|---|---|
| Step wizard (MediaMTX READY → pattern → compare → enter → save) | Partial (buttons + MessageBox, not guided wizard) |
| Bind result to stream **configuration fingerprint** (camera, IP/profile, codec, res, FPS, GOP, path, date, method) | **Weak** — `GlassToGlassSample` has `StreamId` + method + note; qualification has a `ConfigFingerprint` string, but **manual sample is not invalidated on profile change** (`OUTDATED` missing) |
| Persist across CC restarts | In-memory list + reports under `runtime/test-results` when qualification/A-B run |

---

## 9. Detector launch map

| Detector | Entrypoint | Input | Overlay | On detector `main`? | Admin Studio |
|---|---|---|---|---|---|
| Fall | `apps/zahradky_horni_pad.py` | `--input-profile mediamtx_proxy` + env RTSP to local MediaMTX | `--debug-overlay` / `--no-window` | Fall stack lives on feature branches (#1/#2), **not** trivial main-only | Start/Stop/Debug via Detectors tab; START PRODUKCE starts primary fall |
| Barrier | `zahradky_safety/app.py` (+ lower `app_relay.py`) | historically direct camera; CC sets `CABLEGUARD_MEDIAMTX_RTSP_URL` but **app may not consume it the same way** | own OpenCV ROI editor | Present in repo tree | Type allowed in JSON; real parity unverified |
| Publishers | JSONL yes; Telegram env-gated; `EventCorePublisher` **raises / disabled** in YAML (`event_core.enabled: false`) | — | — | Contract reserved | GUI may show Telegram toggles; Event Core publisher **NOT AVAILABLE** honestly in places |

**GUI rule violation risk:** showing detector “AVAILABLE/RUNNING” when:

- feature branch not checked out, or  
- heartbeat/publisher contract missing  

`DetectorProcessManager` already fails if script path missing — good. Status still collapses to process-alive.

---

## 10. Current status flow

### Control Center

| Component | Signals used | Becomes RUNNING when |
|---|---|---|
| MediaMTX | PID file **or** (local) process name / ports; WHEP OPTIONS; path ready API | process/port up + WHEP OK + path ready |
| Event Core | HTTP `/api/v1/health` (+ PID) | health OK |
| Monitor | HTTP health (+ PID) | health OK |
| Detector (overview primary) | python cmdline / managed PID | **process alive only** |
| Detectors tab rows | same | process / configured labels |
| Video Lab “detector freshness” | hardcoded NOT AVAILABLE | honest |
| Video Lab video health | MediaMTX path + optional browser probe FPS/ICE | REALTIME/DEGRADED/… separate from detector |

### Event Core

- `POST /api/v1/heartbeats` with `status ∈ {healthy,degraded,offline}`, optional `camera_connected`, `inference_running`, `details_json`  
- Timeout monitor → offline + history  
- WS updates for Monitor  

### Monitor

- `/system`: service cards from Event Core  
- `/dashboard` / kiosk: `stationService.buildStationStatus` maps service_ids like `zahradky-horni-pad-detector`  
- **Does not** talk to python detectors directly (correct)  
- If fall never heartbeats → shows offline/empty despite CC “RUNNING”

### Failure modes observed / implied

| Situation | Likely UI today |
|---|---|
| python up, camera dead | CC: RUNNING; EC: (none) → Monitor offline |
| python up, frozen frames | CC: RUNNING |
| MediaMTX up, stale PID | CC: STOPPED + start fails (**local fix**) |
| MediaMTX up, camera path not ready | CC: DEGRADED (good) |

---

## 11. Target status contract (proposal — no new status server)

Reuse Event Core `HeartbeatCreate` + extend **`details_json`** (and gradually promote fields) rather than a new HTTP server on the detector.

### Minimum `details_json` / promoted fields

```text
service_id
detector_type                  # fall | barrier
runtime_state                  # NOT_CONFIGURED|STOPPED|STARTING|RUNNING|DEGRADED|STALE|FAULT|STOPPING
process_alive                  # local fact (optional; CC may also know)
camera_connected
input_stream
frames_received                # monotonic
last_frame_at
frame_unchanged                # bool / age — drives STALE
inference_running
inference_fps
last_inference_at
model_id
model_sha256
algorithm_version
roi_profile                    # id of ACTIVE profile
roi_sha256
roi_roles                      # barrier: person/safety_bar/exclude hashes
debug_overlay
publishers.telegram            # DISABLED|OK|FAULT
publishers.event_core          # NOT_AVAILABLE|OK|FAULT
publishers.jsonl
last_error
started_at
service_version
config_fingerprint             # stream+model+roi
```

### State machine (functional)

```text
RUNNING  = process_alive ∧ heartbeat fresh ∧ camera_connected ∧ frames changing ∧ inference_running
DEGRADED = process_alive ∧ heartbeat fresh ∧ (camera issues | publisher issues | partial inference)
STALE    = process_alive ∧ heartbeat fresh ∧ frames not advancing
FAULT    = heartbeat says fault OR WHEP/process contradiction OR repeated errors
STOPPED  = no process / clean stop
STARTING / STOPPING = transitional (CC + first heartbeats)
NOT_CONFIGURED = missing script/branch/config
```

**Control Center** may show **process row** separately from **functional state from Event Core**. Never equate them in one green pill.

---

## 12. Frontend integration proposal

| Surface | Content |
|---|---|
| **Admin Studio – Detectors** | Full technical detail (PID, heartbeat age, FPS, model SHA, ROI saved vs active, publishers, last error) + start/stop/restart |
| **Monitor `/system`** | Same functional fields read-only from Event Core |
| **Monitor `/dashboard`** | Compact: `Pádová detekce horní 🟢 RUNNING` / `🟡 STOPPED` / video health short |
| **Kiosk** | Only operational warnings: `⚠ PÁDOVÁ DETEKCE NENÍ AKTIVNÍ` — no PID/SHA |

**Rule:** Frontend → Event Core only (already mostly true). CC remains the only process controller.

---

## 13. Overengineering / complexity review

| Item | Verdict | Note |
|---|---|---|
| Video Lab soak 1m–4h + fault injection + A/B + qualification | **KEEP** but **SIMPLIFY** labels; defer auto OCR latency | Valuable engineering lab; don’t pretend safety cert |
| Browser WHEP probe file-drop stats | **KEEP** (pragmatic) | Document as optional |
| Automated latency EXPERIMENTAL | **KEEP** muted / **DEFER** UI prominence | |
| Admin Studio Hardware / Telegram test stubs | **DEFER** or mark NOT AVAILABLE | Avoid fake success |
| Scenario runner | **KEEP** thin | Useful; ensure it doesn’t imply production apply |
| ROI editor without apply-to-detector | **SIMPLIFY** messaging now; **SPLIT** apply/hash work later | |
| Parallel status: CC process vs EC heartbeat | **SIMPLIFY** toward EC for functional | Biggest architectural debt |
| Multiple open LAN/WHEP PRs across repos | **SPLIT** / close deferred | Reduces noise around #24 |
| Monitor untracked WHEP scripts | **REMOVE** from merge path / **DEFER** archive | |
| PR #24 scope (Lab + Czech + docs + preview) | **SPLIT** ideal; **KEEP** if commits stay reviewable | Local dirty must be committed as discrete fixes |

---

## 14. Test gaps / acceptance matrix

| # | Scenario | Today | Needed |
|---|---|---|---|
| 1 | Cold start after reboot | Manual | Automated smoke script |
| 2 | MediaMTX running, correct PID | OK | Assert CC Running + start no-op |
| 3 | MediaMTX running, **stale PID** | **Broken on pushed #24; fixed locally** | Unit/script test for adopt |
| 4 | Port 8554 owned by non-mediamtx | start refuses | Keep + assert message |
| 5 | Two mediamtx processes | Undefined / dangerous | Detect & refuse or kill-managed-only |
| 6 | ROI snapshot with **only** MediaMTX | Works if path ready | GUI acceptance |
| 7 | ROI saved vs active | No active linkage | Block “active” claim |
| 8 | Fall detector start | Works on feature branch | Branch gate in UI |
| 9 | Barrier detector start | Partial | Contract test |
| 10 | Process up, camera offline | False RUNNING | Needs heartbeat fields |
| 11 | Process up, frames frozen | False RUNNING | STALE via details |
| 12 | Heartbeat timeout | EC OK if publisher exists | Fall must implement publisher |
| 13 | Monitor WS status update | OK for heartbeating services | E2E with fall publisher |
| 14 | Detector restart | CC stop/start | Assert new PID + heartbeat |
| 15 | Model/ROI change visible | No | After ACTIVE hash in heartbeat |
| 16 | No secrets in logs/status | Mostly (RTSP creds avoided in fall CC launch) | Grep CI for password/token |

---

## 15. Recommended commit plan (do not execute in this audit)

### platform tip of `feature/video-qualification-lab` (before asking for #24 review)

1. `fix(mediamtx): adopt running process and heal stale PID`  
   - `scripts/start_mediamtx.ps1`, `stop_mediamtx.ps1`  
   - `Components.cs`, `WindowsProcessInspector.cs`, `Interfaces.cs`  

2. `fix(video-lab): async resource refresh (no UI-thread deadlock)`  
   - `VideoLabViewModel.cs`  

3. `feat(roi): snapshot from MediaMTX + stream/role editor`  
   - `StreamFrameGrabber.cs`, Calibration VM/XAML/code-behind, `AdminLabModels.roi_role`, `App.xaml.cs`  

Optional follow-up (can stay out of #24):  
4. `ux(video-lab): rename manual G2G wizard + config fingerprint invalidation`  

### detector `feature/mediamtx-input-profile`

5. `fix(fall): size debug overlay window on first frame`  
   - `apps/zahradky_horni_pad.py` → push to PR #2  

### monitor

6. **Do not** commit WHEP diagnostic scripts into #9 without separate decision.  
7. Decide fate of `docs/local-integration.md` local rewrite (commit to #9 **or** discard).  

---

## 16. Recommended PR plan

| PR | Action |
|---|---|
| **platform #24** (draft) | Land commits 1–3 above; update PR body: Video Lab + operator UX fixes + MediaMTX lifecycle; keep draft until acceptance checklist green; **then** ready-for-review |
| **platform** (optional) | If reviewers reject lifecycle in #24 → tiny `fix/mediamtx-stale-pid` PR cherry-picked from commit 1 |
| **detector #2** | Include OpenCV resize commit; keep base on fall feature branch; do not merge to main until #1 strategy clear |
| **monitor #9** | Camera comparison only; quarantine untracked WHEP scripts |
| **Later epic** | Detector → Event Core heartbeat enrichment + Admin Studio functional status + ROI apply/ACTIVE hash — **new PR series**, not #24 |

### Suggested PR #24 acceptance checklist (human)

- [ ] Dirty tree empty or only intentional leftovers  
- [ ] Stale PID reproduce → Start MediaMTX exits 0 / CC shows Running  
- [ ] ROI: MediaMTX-only snapshot works; no Event Core required  
- [ ] Video Lab: Obnovit zdroje does not freeze UI  
- [ ] G2G remains NOT MEASURED until manual entry  
- [ ] 63 control-center unit tests green  
- [ ] No `runtime/` or `publish/` files in PR diff  

---

## 17. GO / NO-GO for PR #24

### **NO-GO to merge as of 2026-07-28**

Reasons:

1. **Working tree dirty** with production-impacting MediaMTX lifecycle and ROI snapshot fixes **not on the PR**.  
2. Merging pushed commits alone would ship a Video Lab that still **deadlocks** on resource refresh and an ROI tab that is still a **black canvas** relative to local intent.  
3. PR already broad; merging without the tip fixes creates a false “done” signal.  
4. Functional detector status / Event Core publisher still absent — OK for #24 **if explicitly scoped out**, but PR text must not imply end-to-end detector observability.

### Path to GO

1. Commit/push plan §15 items 1–3 onto #24.  
2. Refresh PR description + checklist §16.  
3. Manual acceptance on 10.6.1.40 host.  
4. Mark ready-for-review; merge only after review.  
5. Track heartbeat/ROI-ACTIVE as **follow-up epic** (not silent scope of #24).

---

## Appendix A — Exact recommended sequence (commands deferred)

```text
1. platform: commit MediaMTX lifecycle
2. platform: commit Video Lab async resources
3. platform: commit ROI snapshot/editor
4. platform: push feature/video-qualification-lab → update PR #24
5. detector: commit OpenCV resize → push → update PR #2
6. monitor: leave WHEP untracked OR move to archive branch; decide docs/local-integration.md
7. human acceptance on host
8. only then undraft PR #24
```

**This audit did not commit, push, merge, stash, or delete anything.**

---

## Appendix B — Evidence snapshots (2026-07-28)

```text
platform HEAD 2aa96ab == origin/feature/video-qualification-lab
platform dirty: 11 modified + StreamFrameGrabber.cs untracked
detector HEAD 72e74d0; dirty: apps/zahradky_horni_pad.py
monitor HEAD 8d8cc0c; dirty: docs/local-integration.md + 11 untracked scripts
runtime/config/roi seeded: barrier-horni-{person,safety_bar,exclude}.json, office-fall-test.json
publish/ ignored; runtime/ ignored
Event Core HeartbeatCreate already has camera_connected, inference_running, details_json
Fall YAML: publishers.event_core.enabled: false; EventCorePublisher not implemented
```
