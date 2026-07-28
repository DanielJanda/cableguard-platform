# VIDEO_PIPELINE — kamera → prohlížeč

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

---

## Tok

```
kamera (RTSP, H.264)
  → MediaMTX path (RTSP ingest, TCP)
    → WHEP/WebRTC (:8889 HTTP handshake, :8189 UDP media)
      → prohlížeč (<video>, nativní WHEP klient)
```

## Proč prohlížeč nepoužívá RTSP přímo

Prohlížeče RTSP protokol **nepodporují** (žádné nativní API, RTSP typicky vyžaduje TCP 554/UDP RTP a credentials). Přímé použití by znamenalo:

- plugin/transkódovací mezivrstvu v JS (vysoká latence, CPU),
- vystavení RTSP credentials frontendovému kódu — zakázáno.

WebRTC je jediný nativní browser transport se sub-sekundovou latencí. WHEP (WebRTC-HTTP Egress Protocol) standardizuje handshake přes obyčejné HTTP, takže stačí `fetch` + `RTCPeerConnection`.

## MediaMTX path

Path = pojmenovaný stream. Produkční path:

```yaml
paths:
  zahradky-horni-stanice:
    source: rtsp://<credentials>@<camera>/...   # pouze gitignored mediamtx.local.yml
    rtspTransport: tcp
    sourceOnDemand: no    # trvalý ingest, okamžitý start přehrávání
```

MediaMTX se ke kameře připojí jednou a distribuuje všem odběratelům (prohlížeče přes WHEP, detektor přes RTSP proxy `:8554`).

## WHEP handshake (implementace `src/services/whepClient.ts`, MediaMTX v1.11.3)

| Krok | Metoda | Očekáváno | Účel |
|---|---|---|---|
| 1 | `OPTIONS {base}/{path}/whep` | 204 (nebo ok) | preflight; `Link` header s ICE servery |
| 2 | — | — | `RTCPeerConnection`, recvonly transceivery, `createOffer` |
| 3 | `POST` s `Content-Type: application/sdp` | **201** + `Location` | SDP offer → answer; `Location` = session URL |
| 4 | `PATCH {sessionUrl}` s `application/trickle-ice-sdpfrag`, `If-Match` | **204** | trickle ICE kandidáti (queue před získáním session URL, pak flush) |
| 5 | ICE | `connected` | média tečou přes **UDP :8189** |
| 6 | `DELETE {sessionUrl}` | — | úklid session při odpojení (chyby ignorovány) |

**Reconnect:** při selhání exponenciální backoff `min(1000·2^(attempt−1), 30000)` ms; úspěch resetuje čítač; manuální tlačítko „Obnovit video“. React StrictMode je ošetřen generation ref — nikdy nevzniknou dvě souběžné sessions z jednoho playeru.

**Diagnostika** (interval 2 s): `videoWidth/Height`, `fps`, `framesReceived`, `packetsLost`, `bitrateKbps`, `reconnectCount`, `lastConnectedAt`, ICE stav.

## Aktuální funkční tok v LAN (CONFIRMED)

Vše na **`10.6.1.40`**:

```
kamera .92 → MediaMTX (path zahradky-horni-stanice)
  WHEP:   http://10.6.1.40:8889/zahradky-horni-stanice/whep
  player: http://10.6.1.40:8889/zahradky-horni-stanice/
  monitor kiosk: http://10.6.1.40:8080/kiosk/zahradky/horni-stanice
```

CORS: `webrtcAllowOrigin: http://10.6.1.40:8080` (MediaMTX v1.11 podporuje jen jeden origin).

Ověřeno: OPTIONS 204, POST 201, PATCH 204, ICE connected, 1280×720, framesReceived roste; vizuálně z druhého LAN PC.

### Proč teď nepotřebujeme Cloudflare / TURN / veřejnou IP

Platí **výhradně pro internal-LAN deployment**:

- **Cloudflare/doména/TLS:** všichni klienti jsou ve stejné LAN; HTTP na privátní IP stačí a prohlížeč nevyžaduje HTTPS pro WebRTC na HTTP stránce. (HTTPS stránka → HTTP LAN media naopak selhává na Local Network Access — důvod odložení Lovable hostingu.)
- **TURN:** TURN řeší NAT traversal mezi odlišnými sítěmi. V jedné LAN se klient a server vidí přímo (host kandidáti), ICE se spojí bez relay.
- **Veřejná IP / port forwarding:** žádný klient nepřistupuje z internetu. Veřejné vystavení je DEFERRED (platform PR #6, monitor PR #5).

## Multiple cameras

Princip: **jedna kamera = jedna path**.

```
kamera .92 → path zahradky-horni-stanice      (produkce)
kamera .92 → path zahradky-horni-stanice-92   (porovnání, alias)
kamera .90 → path zahradky-horni-stanice-90   (porovnání)
```

- Paths jsou nezávislé: výpadek jedné kamery neovlivní druhou; každý WHEP klient má vlastní `RTCPeerConnection`.
- Porovnávací stránka `/compare/zahradky-horni-stanice` (monitor PR #9, EXPERIMENTAL) zobrazuje dvě paths vedle sebe s nezávislými reconnecty.
- Setup: `scripts/setup_second_zahradky_camera.ps1` (platform PR #10) — přidá comparison paths do gitignored configu bez výpisu credentials.
- Kamery s HEVC main streamem (`.90`) používají H.264 substream — HEVC není browser-kompatibilní.

Budoucí stanice následují stejný vzor: `zahradky-dolni-stanice` atd. (registry v monitoru už místo má, `enabled: false`).

---

## Video Qualification Lab (Admin Studio)

Control Center tab **Video Lab** (`tools/control-center/`) měří realtime vlastnosti pipeline. Zásadní oddělení:

| Vrstva | Co měří | Co to **není** |
|---|---|---|
| **TRANSPORT HEALTH** | path ready, ICE, browser FPS/bitrate/jitter/RTT, freezes, MediaMTX bytes/readers | glass-to-glass latence |
| **CAMERA PROFILE** | codec/resolution/FPS pokud zjistitelné (TCP/ffprobe) | UTC capture timestamp z PTS/DTS |
| **GLASS-TO-GLASS** | pouze Manual Latency Test s fyzickou vizuální referencí | odhad z FPS/RTT/jitteru |
| **DETECTOR FRESHNESS** | frame_received → inference monotonic (až bude contract) | camera capture age / G2G |

### Pravidla (MUST)

1. **WebRTC network metrics ≠ glass-to-glass latency.** RTT, jitter, FPS, MediaMTX counters se **nesmí** prezentovat jako G2G.
2. **Detector runtime frame age ≠ camera capture age.** Queue age uvnitř detektoru není latence kamery.
3. **LIVE ≠ REALTIME.** Monitor `LIVE` = WHEP session connected + frames. Video Lab `REALTIME` vyžaduje path ready + pokračující frames + ne-stale; **ICE connected samo o sobě nestačí**.
4. Bez fyzického Latency Testu UI vždy ukáže: **`GLASS-TO-GLASS LATENCY: NOT MEASURED`**.
5. Automated Gray-code/marker decode je **EXPERIMENTAL** — výsledek se nezobrazuje jako autoritativní.

### MediaMTX metrics (pinned v1.11.3)

Endpoint aktivovat jen lokálně:

```yaml
metrics: yes
metricsAddress: 127.0.0.1:9998
```

Nikdy nevystavovat do LAN. Control Center čte `http://127.0.0.1:9998/metrics`.

V binárce v1.11.3 (string audit `runtime/mediamtx/mediamtx.exe`) jsou přítomné mimo jiné:
`paths`, `paths_bytes_received`, `paths_bytes_sent`, `webrtc_sessions`, `webrtc_sessions_bytes_received`, `metricsAddress`.

**Není** v binárce jako literál (a proto se nespoléháme na ně jako na jistotu v1.11.3):
`paths_inbound_bytes`, `webrtc_sessions_rtp_packets_lost`, `webrtc_sessions_rtp_packets_jitter`.

Reader/session counts a bytes lze vždy číst i z MediaMTX Control API (`/v3/paths/get/{path}`) bez metrics endpointu. Parser v Control Center přijímá i novější aliasy `paths_inbound_bytes` / `paths_outbound_bytes` / `paths_readers` pro forward-compat, ale **zdroj pravdy pro tuto pinovanou verzi** jsou metriky výše + Control API.

Packet loss / jitter / RTT: primárně **browser WHEP probe** (`getStats`). MediaMTX path metrics v1.11.3 nepoužíváme jako náhradu za G2G ani jako falešný packet-loss panel, pokud endpoint danou metriku neposkytne.

### Manual Latency workflow

1. Video Lab → **Open latency pattern** (fullscreen ms + SEQ na monitoru v záběru kamery).
2. Souběžně sledovat přijímaný stream (WHEP probe / Preview).
3. Zadat naměřené ms → **Record Manual Latency** → uloží se jako `MANUAL MEASUREMENT` do reportu.
4. Qualification bez G2G sample → **INCOMPLETE**.

Browser transport samples: **Open WHEP Probe** → Download `probe-stats.json` → uložit do `runtime/video-lab/probe-stats.json` (Control Center soubor periodicky načte). Neodhadovat G2G z těchto čísel.

Reporty: `runtime/test-results/<test-id>/{metadata,summary}.json` (+ `samples.csv` u soak). Secrets se neukládají.
