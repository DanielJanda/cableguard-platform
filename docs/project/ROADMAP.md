# ROADMAP — fázovaný plán vývoje CableGuard

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

Toto není wishlist — fáze vycházejí ze skutečného stavu (viz CURRENT_STATE.md). Časové odhady v dnech záměrně neuvádíme; komplexita S/M/L/XL.

---

## PROJECT_STATUS DASHBOARD

| Phase | Status | Main result |
|---|---|---|
| 0 – Foundation | **DONE** | Tři repozitáře, Event Core kontrakt, monitor UI, MediaMTX runtime, detector baseline |
| 1 – Internal realtime video | **DONE** | Kamera → MediaMTX → WHEP → LAN monitor na 10.6.1.40, acceptance z druhého PC |
| 2 – Camera evaluation | **CURRENT** | Porovnání kamer 10.2.4.92 vs 10.2.4.90, výběr streamu pro detekci |
| 2.5 – Local Admin Control Center MVP | **DONE** | Lokální admin GUI MVP mergnuto (PR #21) |
| 2.6 – Admin Studio / Test Lab | **DONE** (PR #22) | OPERATIONS\|TEST LAB: cameras, streams, detectors, ROI, notifications, hardware, scenarios |
| 2.6b – Video Qualification Lab | **CURRENT** | Video Lab: transport metrics, manual G2G, soak, qualification (≠ estimated latency) |
| 3 – Detector media input | **NEXT** | Detektor trvale čte vybranou MediaMTX path, video-level parita |
| 4 – Live fall event integration | PLANNED | Reálný pád → Event Core → alarm v monitoru (EventCorePublisher, heartbeat, outbox) |
| 5 – Production runtime simplification | PLANNED | React build + statický hosting, BFF server-side, konec Vite dev serveru v provozu |
| 6 – Automatic services | PLANNED | Windows služby, auto-start, recovery, logy |
| 7 – Physical safety I/O | PLANNED | Advantech USB, semafor, siréna, relay safety rules |
| 8 – Second station | PLANNED | Dolní stanice: kamery, detektor, kiosk, service identity |
| 9 – Hardening | PLANNED | Auth, zálohy, observabilita, watchdog, disaster recovery |
| — Public internet access | DEFERRED | Platform PR #6 + monitor PR #5; neplánováno, dokud nevznikne reálná potřeba |

---

# Phase 0 – Foundation

STATUS: **DONE**

- **Objective:** Založit tři repozitáře s čistými kontrakty a základními komponentami.
- **Result:** Event Core (FastAPI+SQLite+Alembic, idempotence, WS, 37 testů), monitor (React/TanStack, mock režim, dashboard/events/system/kiosky), MediaMTX managed runtime (PR #2), detector barrier baseline + LFS modely.
- **Acceptance (splněno):** pytest zelený; monitor verify skripty zelené; simulovaný event → WS → alarm v UI; secrets vynucené (placeholdery odmítnuty).

# Phase 1 – Internal realtime video

STATUS: **DONE** (platform PR #8, monitor PR #7, merged 2026-07-22)

- **Objective:** Živé video kamery v LAN monitoru, celý stack na 10.6.1.40.
- **Result:** kamera .92 → RTSP → MediaMTX → WHEP → nativní player; Event Core 0.0.0.0:8000, monitor 0.0.0.0:8080; firewall skript; orchestrační start.
- **Acceptance (splněno, vizuálně z druhého LAN PC):** dashboard + kiosk, POST 201 / PATCH 204, ICE connected, 1280×720, framesReceived roste, simulovaný alarm + BFF acknowledge, restart MediaMTX → OFFLINE → auto-reconnect LIVE.

# Phase 2 – Camera evaluation

STATUS: **CURRENT** (platform PR #10, monitor PR #9 — draft)

- **Objective:** Porovnat kamery `10.2.4.92` a `10.2.4.90` a vybrat preferovaný stream pro pádovou detekci.
- **Inputs:** Comparison paths `zahradky-horni-stanice-92`/`-90` (nasazeno lokálně), compare stránka `/compare/zahradky-horni-stanice`, benchmark skripty; 20min benchmark už proběhl (obě stabilní, 0 reconnectů).
- **Work:** Vizuální side-by-side posouzení (kvalita, latence, zorné pole vůči ROI), rozhodnutí, merge obou draft PR, zápis rozhodnutí do CURRENT_STATE.md.
- **Dependencies:** Phase 1.
- **Acceptance criteria:** Obě kamery současně LIVE na compare stránce; latence porovnána (společný pohyb/hodiny); stabilita ≥ 20 min bez reconnectu; **vybraný preferovaný stream zapsán**; produkční path `zahradky-horni-stanice` nezměněna během evaluace.
- **Out of scope:** Změna detektoru, změna produkční path, transkódování.
- **Risks:** HEVC main profily (řešeno H.264 substreamy); camera source drift (M3).
- **Estimated complexity:** **S**

# Phase 2.5 – Local Admin Control Center MVP

STATUS: **NEXT** — může běžet paralelně s Phase 3 (nezávislé pracovní proudy)

- **Objective:** Odstranit nutnost běžné administrace CableGuard přes PowerShell na lokálním PC 10.6.1.40 — po restartu Windows musí být na první pohled vidět, co běží, co spustit a kde jsou logy.
- **Inputs:** Existující bezpečné runtime skripty (`start_internal_cableguard.ps1`, MediaMTX start/stop/status, Event Core a monitor start skripty), health endpointy (`/api/v1/health`, WHEP OPTIONS), gitignored MediaMTX local config.
- **Scope MVP (Work):**
  - lokální CableGuard Admin GUI (C# / .NET / WPF, `tools/control-center/`),
  - stav MediaMTX, Event Core, Monitoru, Detectoru — **health-based status, ne jen existence procesu**,
  - START ALL s řízením závislostí (MediaMTX → Event Core → Monitor → Detector) a readiness čekáním,
  - Start / Stop / Restart jednotlivých komponent (re-use PowerShell skriptů),
  - centralizovaný přístup k logům (`runtime/logs/`, live tail, filtrování, redakce secrets),
  - Open Dashboard / Open Kiosk,
  - přehled kamer, test/preview kamer (preview přes MediaMTX built-in player),
  - příprava logického camera → stream mappingu (`runtime/config/cameras.json`, gitignored; credentials ve Windows Credential Manager).
- **Dependencies:** žádné tvrdé; Phase 2 běží paralelně.
- **Acceptance criteria:** PC restart use case — admin spustí Control Center, GUI ukáže STOPPED, klik START ALL nastartuje stack v pořadí s readiness kontrolami, SYSTEM READY, Open Kiosk zobrazí živou kameru — **bez otevření PowerShellu**.
- **Out of scope:** Windows Supervisor Service / auto-start (Phase 6), změny fall detection thresholds, ovládání relé/semaforu, změny AI modelu či safety logiky, změny detector configu.
- **Risks:** duplikace start logiky (mitigace: volat existující skripty), MediaMTX v1.11.3 Control API omezení pro runtime path switching.
- **Estimated complexity:** **M**

**Odlišení od Phase 6:** Phase 2.5 = **ruční administrační GUI** pro vývojáře/admina (člověk kliká). Phase 6 = **autonomní** start po rebootu, recovery, Windows Services. Control Center nesmí být podmínkou běhu systému — služby musí fungovat i bez něj.

# Phase 2.6 – Admin Studio / Test Lab

STATUS: **DONE** (platform PR #22)

- **Objective:** Odstranit nutnost ručních úprav Python/YAML/PowerShell při běžném vývoji a testování (kamery v kanceláři, detector profily, ROI, Telegram, hardware test).
- **Work:** Dual mode OPERATIONS|TEST LAB; tabs Scenarios/Cameras/Streams/Detectors/Calibration/Notifications/Hardware; gitignored `runtime/config/*`; detector launch přes MediaMTX logical streams; ROI profily; hardware TEST MODE guardrails (bez auto vazby detector→relay).
- **Out of scope:** editace ANGLE/TORSO/MOV/AXIS/weights/risk threshold; auto safety I/O (Phase 7); embedded WebRTC inference player (MVP = OpenCV debug overlay).
- **Estimated complexity:** **L**

# Phase 2.6b – Video Qualification Lab

STATUS: **CURRENT** (issue #23, branch `feature/video-qualification-lab`)

- **Objective:** Objektivně měřit realtime vlastnosti video pipeline a odhalit „LIVE, ale zpožděný obraz“.
- **Work:** Admin Studio tab Video Lab — Live Metrics, Camera Profiles, Manual Latency Test, Detector Freshness (schema/NOT AVAILABLE), Soak, Failure injection, Qualification reports, config fingerprint; MediaMTX metrics jen `127.0.0.1:9998`.
- **Hard rules:** WebRTC metrics ≠ G2G; detector frame age ≠ capture age; LIVE ≠ REALTIME; bez fyzického latency testu vždy `GLASS-TO-GLASS LATENCY: NOT MEASURED`; žádná změna fall algorithm / frame pipeline.
- **Acceptance criteria:** Unit testy health/G2G/qualification/soak/fingerprint zelené; GUI odděluje TRANSPORT / G2G / DETECTOR; qualification bez G2G → INCOMPLETE; automated latency zůstane EXPERIMENTAL.
- **Estimated complexity:** **L**

# Phase 3 – Detector media input

STATUS: **NEXT**

- **Objective:** Detektor trvale konzumuje vybranou MediaMTX path (`mediamtx_proxy` profil) místo přímého připojení na kameru.
- **Inputs:** detector `feature/mediamtx-input-profile` (draft PR #2 — input profil hotový, integračně ověřen ~8,3 FPS); výběr kamery z Phase 2; golden master 46 testů.
- **Work:** Merge fall baseline + MediaMTX input do detector `main`; nastavit produkční input config na vybranou path; side-by-side ověření detekčního chování vs. legacy na stejném streamu (video-level parita, riziko H4); wrap `track()` výjimek + auto-restart smyčky; dlouhodobý běh (≥ hodiny) se stabilní inference FPS.
- **Dependencies:** Phase 2 (výběr streamu).
- **Acceptance criteria:** Detektor běží ≥ 4 h z MediaMTX path bez pádu; golden master stále zelený; side-by-side s legacy bez rozdílu v emitovaných událostech na testovací sekvenci; výpadek kamery/MediaMTX → detektor se sám reconnectne.
- **Out of scope:** Event Core publikace (Phase 4), relé.
- **Risks:** frame pipeline parita (H4), GPU fallback (L1).
- **Estimated complexity:** **M**

# Phase 4 – Live fall event integration

STATUS: PLANNED

- **Objective:** Reálný pád detekovaný kamerou → Event Core → alarm v monitoru, bez simulátoru.
- **Inputs:** detector `feature/fall-event-core-integration` (EventCorePublisher s outbox/retry — hotový na větvi), Event Core kontrakt (idempotentní ingest), heartbeat API.
- **Work:** Merge EventCorePublisher; zapnout `publishers.event_core.enabled: true` s ingest key z env; heartbeaty se stavem (`camera_connected`, `inference_running`, device, FPS); outbox pro výpadky Event Core; naplnit `algorithm_version`, `model_sha256`, `config_sha256` z manifestu; ošetřit 409 po acku (L3); end-to-end test s bezpečně simulovaným pádem před kamerou (figurína/hraný pád mimo provoz).
- **Dependencies:** Phase 3.
- **Acceptance criteria:** Kontrolovaný pád → alarm v kiosku < 2 s; acknowledge zpět; výpadek Event Core → eventy z outboxu doručeny po obnově bez duplikátů; `/system` ukazuje detektor healthy s heartbeaty; JSONL publisher dál běží jako lokální záloha.
- **Out of scope:** Fyzická signalizace (Phase 7), více stanic.
- **Risks:** duplicitní alarmy při retry (kryto idempotencí), správné severity mapování.
- **Estimated complexity:** **M**

# Phase 5 – Production runtime simplification

STATUS: PLANNED

- **Objective:** Odstranit Vite dev server jako provozní závislost (rizika C1, H1).
- **Inputs:** `npm run build` funguje (verify:secrets buildí); FastAPI platforma běží trvale.
- **Work:** React produkční build → statický hosting; **BFF přesunout server-side do platformy** (FastAPI endpoint `/bff/events/{id}/acknowledge` s kiosk-key validací — doporučeno servírovat frontend přímo FastAPI přes StaticFiles: jeden proces, jeden port, žádný CORS; alternativa malý webserver (Caddy/nginx) jen pokud FastAPI serving narazí na limity); upravit `videoMode` resoluci pro produkční internal-lan build; aktualizovat start skripty a OPERATIONS.md.
- **Dependencies:** žádné tvrdé (nezávislá na 3–4); doporučeno po Phase 4, ať se nemění dvě věci najednou.
- **Acceptance criteria:** Monitor běží z produkčního buildu bez `vite dev`; acknowledge funguje přes platformní BFF; verify:secrets PASS na nasazeném buildu; acceptance z druhého PC zopakována.
- **Out of scope:** TLS/auth (Phase 9).
- **Risks:** regrese `videoMode` logiky (PROD placeholder pojistka), rozdíl chování dev vs. build.
- **Estimated complexity:** **M**

# Phase 6 – Automatic services

STATUS: PLANNED

- **Objective:** Systém přežije reboot bez lidského zásahu (riziko C2).
- **Inputs:** Phase 5 (produkční runtime bez dev serveru), existující start/stop/status skripty (+ maintenance PR #9/#8).
- **Work:** MediaMTX, Event Core, monitor hosting a detektor jako Windows služby (NSSM/sc/Task Scheduler) s restart-on-failure; pořadí startu (MediaMTX → Event Core → monitor → detektor); file logging s rotací pro všechny; instalační skript (admin: služby + firewall); konsolidace PowerShell skriptů.
- **Dependencies:** Phase 5 (jinak by se služby stavěly nad dev serverem).
- **Acceptance criteria:** Reboot PC → do N minut vše LIVE bez zásahu; kill libovolné služby → automatická obnova; logy dohledatelné po pádu.
- **Out of scope:** Vzdálený monitoring/notifikace (Phase 9).
- **Risks:** služby pod jiným účtem (env, GPU přístup detektoru, network profily).
- **Estimated complexity:** **L**

# Phase 7 – Physical safety I/O

STATUS: PLANNED

- **Objective:** Fyzická signalizace alarmu: semafor, siréna přes Advantech USB-4761 relé.
- **Inputs:** existující relay subsystem (barrier produkce: `advantech_relay.py`, `relay_server.py`, `safety-invariants.md`), Event Core eventy (Phase 4).
- **Work:** Definovat **relay safety rules** (co smí sepnout, kdy, fail-safe stav, manuální override, timeout) — **fall detektor nesmí bez definované safety logiky přímo ovládat relé** (invariant); I/O služba jako samostatný konzument Event Core (WS/REST), ne přímá vazba detektor→relé; heartbeat `relay_connected`; test s fyzickým zařízením mimo provoz.
- **Dependencies:** Phase 4 (živé eventy), Phase 6 (spolehlivý běh služeb — signalizace nesmí „zmizet“ po rebootu).
- **Acceptance criteria:** Alarm → signalizace do definovaného času; acknowledge → definované chování signalizace; výpadek I/O služby → fail-safe stav + `service.offline`; safety rules zdokumentovány a review-ovány.
- **Out of scope:** Zastavování lanovky či zásah do řízení technologie.
- **Risks:** nejvyšší safety dopad v projektu — vyžaduje formální review; hardware dostupnost.
- **Estimated complexity:** **L**

# Phase 8 – Second station

STATUS: PLANNED

- **Objective:** Rozšířit dohled na dolní stanici Zahrádky (a případné další kamery).
- **Inputs:** path-per-camera model (VIDEO_PIPELINE.md), camera registry v monitoru (`zahradky-dolni-stanice` připraveno, disabled), service identity v Event Core (`service_id`, `station_id`).
- **Work:** Kamera dolní stanice → nová MediaMTX path; druhá detector instance/konfigurace s vlastní `service_id` a ROI; kiosk dolní stanice s videem; dashboard/system rozšíření; zobecnění setup skriptů (M4); kapacitní ověření PC (2× inference — GPU!).
- **Dependencies:** Phase 4 (event integrace); Phase 6 doporučena.
- **Acceptance criteria:** Obě stanice současně LIVE + detekce; eventy správně přiřazené stanicím; výpadek jedné stanice neovlivní druhou.
- **Out of scope:** Další lokality mimo Zahrádky.
- **Risks:** GPU kapacita pro dvě inference; správa více ROI konfigurací.
- **Estimated complexity:** **L**

# Phase 9 – Hardening

STATUS: PLANNED

- **Objective:** Provozní odolnost a bezpečnost odpovídající trvalému bezpečnostnímu systému.
- **Inputs:** rizika H2, H3, M2 (RISKS_AND_TECH_DEBT.md), Phase 6 služby.
- **Work:** Auth pro GET/WS (tokeny/session) + revize trusted-LAN modelu; automatické zálohy SQLite + test obnovy; observabilita (centrální logy, metriky, watchdog s notifikací — např. Telegram, který už detektor umí); disaster recovery procedura (nové PC → running system z dokumentace + záloh); deployment procedura s checklistem; vypnout HLS `:8888`, zvážit RTSP bind na localhost.
- **Dependencies:** Phase 6.
- **Acceptance criteria:** Obnova z čistého PC podle dokumentace; záloha→obnova DB ověřena; neautentizovaný klient nečte eventy; výpadek služby vyvolá notifikaci.
- **Out of scope:** Veřejný internet (zůstává DEFERRED, dokud nevznikne požadavek — pak samostatná fáze nad Phase 9 základem).
- **Risks:** auth změna se dotkne monitoru i detektoru současně — nutná koordinace kontraktů.
- **Estimated complexity:** **XL**
