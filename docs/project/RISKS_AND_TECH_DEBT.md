# RISKS_AND_TECH_DEBT — rizika a technický dluh

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

Kritické zhodnocení. Fakt z 2026-07-27: po auditu žádná služba neběžela (porty 8000/8080/8889 zavřené) — systém se po ukončení procesů sám neobnoví.

---

## CRITICAL

### C1 — Vite dev server jako provozní runtime monitoru

- **Impact:** Provoz operátorského UI stojí na vývojovém nástroji: bez optimalizovaného buildu, s HMR, vyšší pamětí, bez garancí stability při dlouhém běhu; pád dev serveru = operátoři bez dohledu.
- **Likelihood:** Vysoká (nepřetržitý běh dev serveru není jeho design).
- **Current mitigation:** Skripty hlídají duplicitní procesy; restart je jednoduchý (OPERATIONS.md); mock/placeholder fallback.
- **Recommended action:** Phase 5 — produkční React build + statické servírování; BFF přesunout server-side do platformy.

### C2 — Žádný process manager / auto-start po rebootu

- **Impact:** Po restartu PC (Windows update, výpadek napájení) neběží nic — video, alarmy ani acknowledge — dokud někdo ručně nespustí skripty. Pro bezpečnostní systém nepřijatelné v ostrém provozu.
- **Likelihood:** Jistota (reboot nastane).
- **Current mitigation:** `start_internal_cableguard.ps1` startuje vše jedním příkazem; ale vyžaduje člověka.
- **Recommended action:** Phase 6 — Windows služby (NSSM/Task Scheduler/service wrapper) s recovery policy pro MediaMTX, Event Core, monitor, detektor.

## HIGH

### H1 — BFF žije v dev middleware

- **Impact:** Acknowledge (jediná operace s kiosk key) závisí na `vite.config.ts` `configureServer` — v produkčním statickém buildu BFF neexistuje; váže Phase 5.
- **Likelihood:** Jistá při přechodu na produkční build.
- **Current mitigation:** Dev server běží záměrně; contract test hlídá BFF cestu.
- **Recommended action:** Přesun BFF do platformy (FastAPI endpoint s kiosk-key validací) v Phase 5, teprve pak statický hosting.

### H2 — Žádná záloha databáze

- **Impact:** Ztráta `data/cableguard.sqlite3` = ztráta celé historie eventů a acknowledgements (auditní stopa bezpečnostního systému).
- **Likelihood:** Střední (disk failure, omyl).
- **Current mitigation:** Žádná; jen WAL journal na stejném disku.
- **Recommended action:** Phase 9 (případně dřív — jednoduchý denní copy job je levný): plánovaná záloha souboru + ověření obnovy.

### H3 — Event Core GET/WS bez autentizace (trusted-LAN)

- **Impact:** Kdokoli v LAN čte eventy, stav služeb a WS stream; může i DoS-ovat. Zápis je chráněn klíči, čtení ne.
- **Likelihood:** Nízká ve firemní LAN, ale roste s počtem stanic/uživatelů.
- **Current mitigation:** Vědomé rozhodnutí (ADR-005, SECURITY.md); firewall jen Domain/Private; žádná citlivá osobní data v eventech.
- **Recommended action:** Phase 9 — read auth (token/session) + omezení WS originů; do té doby nerozšiřovat LAN expozici.

### H4 — Frame pipeline parity detektoru neověřena na živém videu

- **Impact:** Nová frame pipeline vybírá jiné framy než legacy (čeká na další vs. drží nejnovější) → na živé RTSP může být detekční chování odlišné od ověřené produkce, přestože golden master (numerika) prochází. Navíc výjimka z `track()` může shodit proces (legacy pokračovala).
- **Likelihood:** Střední.
- **Current mitigation:** Parita zdokumentována (`docs/audits/fall-algorithm-parity.md`); golden master kryje numeriku.
- **Recommended action:** Phase 3 — side-by-side běh nové app a legacy na stejném streamu, srovnání emitovaných eventů; wrap `track()` výjimek + auto-restart.

### H5 — MediaMTX konfigurace mimo Git

- **Impact:** `mediamtx.local.yml` (jediný zdroj pravdy pro kamery a credentials) existuje jen na disku `10.6.1.40`; ztráta disku = rekonstruovat ručně; drift mezi example a local je neviditelný pro review.
- **Likelihood:** Střední.
- **Current mitigation:** `mediamtx.example.yml` synchronizovaná struktura; setup skripty umí config regenerovat; credentials dostupné u kamer/správce.
- **Recommended action:** Šifrovaná záloha local configu mimo PC (spolu s H2); example udržovat strukturně identickou.

## MEDIUM

### M1 — Firewall setup vyžaduje admin a je manuální

- **Impact:** Na novém/přeinstalovaném PC bez admin běhu skriptu služby z LAN nejsou vidět; symptom (timeout) se špatně diagnostikuje.
- **Likelihood:** Nízká–střední (jednorázově per PC).
- **Current mitigation:** `ensure_internal_firewall.ps1` idempotentní, s jasným varováním bez admin práv; diagnostický strom v OPERATIONS.md.
- **Recommended action:** Zahrnout do Phase 6 instalační procedury (služby se instalují jako admin — firewall přidat tamtéž).

### M2 — Logging a monitoring bez centrálního místa

- **Impact:** Logy jsou roztroušené (uvicorn konzole, MediaMTX runtime/, Vite konzole, detector runtime/); po pádu procesu konzolové logy mizí; nikdo není notifikován, že služba spadla (heartbeat monitor hlídá jen služby, které heartbeaty posílají — monitor a MediaMTX ne).
- **Likelihood:** Vysoká (běžný provozní stav).
- **Current mitigation:** Event Core `service.offline` pro heartbeatující služby; status skripty na vyžádání.
- **Recommended action:** Phase 6 — file logging s rotací pro všechny služby; Phase 9 — watchdog/notifikace.

### M3 — Camera source drift

- **Impact:** Kamerové zdroje (IP, profily, credentials) se mění mimo Git; detector `.env` a `mediamtx.local.yml` mohou mít odlišné zdroje pro „stejnou“ kameru → detektor a operátor koukají na jiný stream.
- **Likelihood:** Střední (právě probíhá evaluace .92 vs .90).
- **Current mitigation:** Phase 2 srovnání kamer; VIDEO_PIPELINE.md dokumentuje mapování path↔kamera.
- **Recommended action:** Po Phase 2 zafixovat vybranou kameru; od Phase 3 detektor odebírá z MediaMTX path (jediný zdroj pravdy), ne přímo z kamery.

### M4 — Multiple cameras škálování

- **Impact:** Každá další kamera = ruční edit local configu + nová path + firewall/CPU dopad; `webrtcAllowOrigin` je jediný string; UI registry se edituje v kódu.
- **Likelihood:** Jistá při Phase 8 (dolní stanice).
- **Current mitigation:** Setup skript pro druhou kameru existuje (PR #10); path model škáluje technicky dobře.
- **Recommended action:** Při Phase 8 zobecnit setup skript a camera registry (config-driven místo hardcoded).

### M5 — Omezení headless WebRTC testů

- **Impact:** Plný video acceptance nejde automatizovat (headless ICE/dekódování nespolehlivé, Playwright testy občas visí) → regrese videa se pozná až manuálně.
- **Likelihood:** Trvalé omezení nástrojů.
- **Current mitigation:** Handshake-level testy (OPTIONS/POST/PATCH) automatizované; benchmark přes MediaMTX API bez prohlížeče; manuální vizuální checklist.
- **Recommended action:** Akceptovat; držet handshake testy + krátký manuální checklist po každém merge dotýkajícím se videa.

## LOW

### L1 — GPU fallback detektoru

- **Impact:** Bez CUDA spadne inference na CPU (half=False) → nižší FPS, jiná latence detekce; tiché zpomalení místo chyby.
- **Likelihood:** Nízká (produkční PC má GPU).
- **Current mitigation:** Device volba logována; `inference_running`/FPS bude v heartbeatech (Phase 4).
- **Recommended action:** V Phase 4 heartbeat s device+FPS; alert při poklesu pod práh.

### L2 — Úklid experimentálních skriptů a deferred větví

- **Impact:** ~10 untracked `whep-*.mjs` diagnostických skriptů v monitoru a stárnoucí deferred PR matou nové čtenáře.
- **Likelihood:** Kosmetické.
- **Current mitigation:** Dokumentace (tento audit) je označuje.
- **Recommended action:** Smazat jednorázové skripty; deferred PR zavřít s komentářem, pokud se veřejný přístup nepotvrdí do 2 fází.

### L3 — Idempotence eventu po acknowledgi

- **Impact:** Retry `POST /events` téhož `event_id` po acku vrací 409 (uložený stav `acknowledged` ≠ kanonický `open`) — publisher s dlouhým retry oknem by logoval falešné konflikty.
- **Likelihood:** Nízká (retry okna jsou krátká).
- **Current mitigation:** Zdokumentováno (EVENT_PIPELINE.md); outbox publisher na detector větvi řeší doručení před ack oknem.
- **Recommended action:** Při Phase 4 v publisheru považovat 409 po předchozím úspěchu za doručeno.
