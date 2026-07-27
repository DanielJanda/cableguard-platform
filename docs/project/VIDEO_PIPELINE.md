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
