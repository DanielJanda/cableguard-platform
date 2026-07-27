# DECISIONS — Architecture Decision Log

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

Žádné rozhodnutí není definitivní navždy — každé ADR může být později označeno **Superseded** s odkazem na nástupce.

---

## ADR-001 — Three repositories

- **Context:** Systém má tři odlišné domény (AI inference, event backend, operátorské UI) s odlišnými technologiemi, tempem vývoje a nástroji (Lovable pro UI, CUDA pro detektor).
- **Decision:** Tři samostatné repozitáře: `cableguard-detector`, `cableguard-platform`, `cableguard-monitor`.
- **Reason:** Nezávislé release cykly, čisté hranice kontraktů (REST/WS/WHEP), Lovable synchronizace jen s monitorem, detektor s LFS modely neroste do ostatních repos.
- **Consequences:** Kontrakty musí být dokumentované a testované (contract testy); změny napříč vyžadují koordinované PR; dokumentace centralizována v platform `docs/project/`.
- **Status:** Accepted

## ADR-002 — MediaMTX as media router

- **Context:** RTSP kamery je třeba distribuovat do prohlížečů a detektoru; prohlížeč RTSP neumí.
- **Decision:** MediaMTX (v1.11.3) jako jediný media router: RTSP ingest → WHEP egress + RTSP proxy.
- **Reason:** Single binary bez závislostí (Windows), jeden YAML config, nativní RTSP+WHEP, jedno připojení na kameru pro N odběratelů. Detailní posouzení v ARCHITECTURE.md.
- **Consequences:** Konfigurace s credentials žije mimo Git (`mediamtx.local.yml`); single instance bez HA; `webrtcAllowOrigin` podporuje jen jeden origin.
- **Status:** Accepted

## ADR-003 — WebRTC/WHEP for browser realtime video

- **Context:** Operátor potřebuje sub-sekundovou latenci v běžném prohlížeči; HLS má latenci v sekundách, iframe embed nedává kontrolu nad UX/diagnostikou.
- **Decision:** Nativní WHEP klient (fetch + RTCPeerConnection) v monitoru, bez externí knihovny.
- **Reason:** WebRTC je jediný nativní nízkolatentní browser transport; WHEP standardizuje handshake; vlastní klient umožnil přesné sladění s MediaMTX v1.11.3 (POST 201, If-Match PATCH) a plnou diagnostiku.
- **Consequences:** Klient je citlivý na verzi MediaMTX; headless testování WebRTC je omezené (manuální vizuální acceptance zůstává); iframe režim zachován jen jako debug fallback.
- **Status:** Accepted

## ADR-004 — Event Core separated from video plane

- **Context:** Události (alarmy, health) a video mají zcela odlišné požadavky (persistence + auditovatelnost vs. throughput + latence).
- **Decision:** Event Core (FastAPI+SQLite) nikdy nepřenáší video; nese jen metadata (`snapshot_url`, `clip_url`). Video jde přímo MediaMTX → prohlížeč.
- **Reason:** Výpadek jedné roviny neshodí druhou; Event Core zůstává malý, testovatelný (37 unit/integration testů), bez media závislostí.
- **Consequences:** Monitor spojuje dva zdroje (WS eventy + WHEP video) sám; korelace event↔video snímek se řeší metadaty.
- **Status:** Accepted

## ADR-005 — Internal LAN deployment first

- **Context:** Uživatelé jsou výhradně ve firemní LAN; veřejný hosting (Lovable/Cloudflare/TURN) přinesl komplexitu a blokace (HTTPS→HTTP Local Network Access) bez provozního přínosu.
- **Decision:** Celý stack běží na PC `10.6.1.40`, dostupný jen z LAN; public HTTPS větve DEFERRED.
- **Reason:** Jednodušší provoz, nulová závislost na internetu, žádné TURN/TLS/doména; potvrzeno acceptance z druhého PC.
- **Consequences:** Trusted-LAN security model (GET/WS bez auth); vystavení do internetu vyžaduje Phase 9; deferred PR se udržují jen jako reference.
- **Status:** Accepted

## ADR-006 — Lovable as frontend development tool, not runtime host

- **Context:** Monitor vznikl v Lovable; lovable.app hosting ale nemůže přehrávat HTTP LAN video (browser Local Network Access) a nemá přístup k Event Core v LAN.
- **Decision:** Lovable slouží pro UI vývoj a Git synchronizaci; runtime je lokální checkout `main` na `10.6.1.40`.
- **Reason:** Mock režim umožňuje plnohodnotný UI vývoj v Lovable; interní deployment je jen `git pull` + restart.
- **Consequences:** „Publish → Update“ v Lovable není součást deploymentu; PROD build mimo `internal-lan` režim vynucuje placeholder video (pojistka proti úniku interních URL do veřejného buildu).
- **Status:** Accepted

## ADR-007 — Detector algorithm protected by golden master

- **Context:** Fall algoritmus je přenesen z ověřené legacy implementace; runtime okolo něj (vstupy, publishery) se bude dál vyvíjet a nesmí tiše změnit detekční chování.
- **Decision:** Numerická logika zmrazena golden-master testy (`keypoint_sequences.jsonl` → `expected_results.jsonl`, tolerance 1e-9).
- **Reason:** Bezpečnostní systém — regrese detekce je nepřijatelná; golden master ji zachytí při každém pytest běhu.
- **Consequences:** Vědomá změna parametrů vyžaduje regeneraci golden datasetu; video-level parita (frame pipeline) zůstává mimo záběr golden masteru a musí se ověřit zvlášť (Phase 3).
- **Status:** Accepted

## ADR-008 — Secrets server-side only

- **Context:** Frontend bundle je čitelný komukoli v LAN; RTSP credentials a API klíče v něm nesmí být.
- **Decision:** Všechny secrets žijí v env/gitignored souborech; prohlížeč nikdy nedrží klíče (BFF injektuje `X-Kiosk-Key`), RTSP zná jen MediaMTX/detektor. Vynuceno build scanem a runtime validací placeholderů.
- **Reason:** Minimalizace úniku; klíč nelze zapomenout „natvrdo“ — systém bez konfigurace odmítne běžet (503).
- **Consequences:** Acknowledge vyžaduje server-side vrstvu (dnes Vite BFF → Phase 5 přesun do platformy); onboarding vyžaduje ruční založení `.local` souborů podle `*.example`.
- **Status:** Accepted

## ADR-009 — Local Admin Control Center for development and administration

- **Context:** Běžná administrace (start/stop/status služeb, logy, správa kamerových zdrojů) dnes vyžaduje PowerShell a znalost mnoha skriptů; po restartu Windows není stav systému na první pohled viditelný (riziko M5).
- **Decision:** Vzniká lokální **CableGuard Control Center** (C#/.NET/WPF, `tools/control-center/`) — pomocné administrační GUI pro správce na 10.6.1.40. **Není to operátorský monitor** (ten zůstává cableguard-monitor) **a není to Supervisor Service** (autonomní služby jsou Phase 6). V MVP re-usuje existující PowerShell runtime skripty.
- **Reason:** Odstranit PowerShell bariéru pro standardní administraci; health-based přehled stavu; bezpečná správa camera→stream mappingu bez ručních editací YAML.
- **Consequences:** Nová admin plane vrstva (viz ARCHITECTURE.md) — nesmí zasahovat do safety logiky ani být runtime závislostí; camera registry (`runtime/config/cameras.json`, gitignored) + Windows Credential Manager pro credentials; dlouhodobě může start logika přejít ze skriptů do C#.
- **Status:** Accepted


