# MediaMTX (example only)

This folder contains **example** configuration for a future local MediaMTX deployment.

- Do **not** put real RTSP credentials here.
- MediaMTX is **not** started by CableGuard Platform scripts yet.
- Stream names: `zahradky-horni-stanice`, `zahradky-dolni-stanice`, `testovaci-kancelar-kamera-1`.
- WHEP shape: `{WHEP_BASE_URL}/{stream_name}/whep`

Event Core does not proxy video. The UI should connect to MediaMTX WHEP directly.
