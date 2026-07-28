# NETWORK_AND_PORTS — autoritativní tabulka portů

Last verified: 2026-07-27

Verified against:

- cableguard-platform `main` commit `5400cb3`
- cableguard-monitor `main` commit `f085ef0`
- cableguard-detector `main` commit `c628a2f`

Vše běží na PC **`10.6.1.40`** (Windows). Firewall pravidla (Domain + Private profily) zajišťuje `scripts/ensure_internal_firewall.ps1`.

---

## Internal LAN

| Port | Protokol | Služba | Bind | Používá | Dostupný z LAN? | Localhost-only? | Nutný pro provoz? |
|---|---|---|---|---|---|---|---|
| **8000** | TCP | Event Core (uvicorn) | `0.0.0.0` (`start_internal_event_core.ps1`) | monitor BFF, WS klienti, detektor/simulátor, kiosky | **ANO** | ne | **ANO** |
| **8080** | TCP | Monitor (Vite dev + BFF) | `0.0.0.0` (`start_internal_monitor.ps1`) | prohlížeče operátorů | **ANO** | ne | **ANO** |
| **8889** | TCP | MediaMTX WHEP/HTTP | všechna rozhraní (`webrtcAddress :8889`) | WHEP handshake z prohlížečů, vestavěný player | **ANO** | ne | **ANO** |
| **8189** | UDP | MediaMTX WebRTC ICE | všechna rozhraní | média WebRTC do prohlížečů | **ANO** | ne | **ANO** |
| **8554** | TCP | MediaMTX RTSP proxy | všechna rozhraní | detektor (`mediamtx_proxy` input profil) — lokálně | ne (stačí localhost; firewall pravidlo pro něj nezakládáme) | prakticky ano | ANO od Phase 3 (detektor) |
| **9997** | TCP | MediaMTX API | `127.0.0.1` | diagnostika, status skripty | **NE** | **ANO** | ne (jen diagnostika) |
| **8888** | TCP | MediaMTX HLS | všechna rozhraní (v configu), nepoužíváno | — | ne | mělo by být | **NE** — kandidát na vypnutí |
| 9998 | TCP | MediaMTX metrics | `127.0.0.1`, vypnuto | — | ne | ano | ne |

Poznámky:

- Firewall skript otevírá pouze **8080, 8000, 8889 TCP + 8189 UDP** pro Domain/Private profily; Public profil se neotvírá.
- MediaMTX v1.11.3 binduje `:PORT` na všechna rozhraní; omezení expozice 8554/8888 zajišťuje absence firewall pravidel, ne bind. Doporučení (Phase 9): HLS vypnout v configu, RTSP bind zvážit na `127.0.0.1`, pokud detektor poběží na stejném PC.
- Dev-only alternativa monitoru: `:5173` (`npm run dev` bez internal-lan). Event Core má jediný entrypoint `start_internal_event_core.ps1`.

## Public internet — currently not used

Žádný port není vystaven do internetu. Veřejné experimenty jsou **DEFERRED**:

| Co | Kde | Stav |
|---|---|---|
| Public HTTPS WHEP (Caddy, TLS, doména, port forwarding) | platform PR #6 | DEFERRED — nemergovat |
| Production env pro public WHEP monitor | monitor PR #5 | DEFERRED — nemergovat |
| Lovable.app jako runtime hosting | — | DEFERRED — HTTPS→HTTP LAN blokováno prohlížečem (Local Network Access) |
| TURN server | — | nepotřebný v jedné LAN (viz VIDEO_PIPELINE.md) |
