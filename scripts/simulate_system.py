#!/usr/bin/env python3
"""Simulate CableGuard detectors / IO / MediaMTX against Event Core.

No camera, GPU, model, relay, or USB required.
"""

from __future__ import annotations

import argparse
import sys
import time
import uuid
from datetime import datetime, timezone

import httpx

DEFAULT_BASE = "http://127.0.0.1:8000"

SERVICES = [
    {
        "service_id": "zahradky-horni-pad-detector",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_type": "fall_detector",
        "camera_connected": True,
        "inference_running": True,
        "relay_connected": None,
    },
    {
        "service_id": "zahradky-horni-zabrana-detector",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_type": "safety_bar_detector",
        "camera_connected": True,
        "inference_running": True,
        "relay_connected": True,
    },
    {
        "service_id": "zahradky-dolni-zabrana-detector",
        "site_id": "zahradky",
        "station_id": "dolni_stanice",
        "service_type": "safety_bar_detector",
        "camera_connected": True,
        "inference_running": True,
        "relay_connected": True,
    },
    {
        "service_id": "zahradky-io-agent",
        "site_id": "zahradky",
        "station_id": None,
        "service_type": "io_agent",
        "camera_connected": None,
        "inference_running": None,
        "relay_connected": True,
    },
    {
        "service_id": "mediamtx",
        "site_id": "zahradky",
        "station_id": None,
        "service_type": "mediamtx",
        "camera_connected": True,
        "inference_running": None,
        "relay_connected": None,
    },
]


def utcnow_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def headers(api_key: str) -> dict[str, str]:
    return {"X-API-Key": api_key, "Content-Type": "application/json"}


def kiosk_headers(kiosk_key: str) -> dict[str, str]:
    return {"X-Kiosk-Key": kiosk_key, "Content-Type": "application/json"}


def send_heartbeat(client: httpx.Client, base: str, api_key: str, svc: dict, status: str = "healthy") -> None:
    body = {
        "service_id": svc["service_id"],
        "site_id": svc["site_id"],
        "station_id": svc["station_id"],
        "service_type": svc["service_type"],
        "status": status,
        "camera_connected": svc["camera_connected"],
        "inference_running": svc["inference_running"],
        "relay_connected": svc["relay_connected"],
        "sent_at": utcnow_iso(),
    }
    r = client.post(f"{base}/api/v1/heartbeats", headers=headers(api_key), json=body)
    r.raise_for_status()
    print(f"heartbeat {svc['service_id']} -> {status}")


def send_event(client: httpx.Client, base: str, api_key: str, event_type: str, event_id: str | None = None) -> str:
    eid = event_id or str(uuid.uuid4())
    mapping = {
        "fall_risk_detected": ("zahradky-horni-pad-detector", "horni_stanice", "kamera4", "alarm", 0.82),
        "safety_bar_alarm": ("zahradky-horni-zabrana-detector", "horni_stanice", "kamera-horni", "alarm", None),
        "camera_offline": ("mediamtx", "horni_stanice", None, "warning", None),
        "io_fault": ("zahradky-io-agent", "horni_stanice", None, "critical", None),
    }
    service_id, station_id, camera_id, severity, risk = mapping[event_type]
    body = {
        "event_id": eid,
        "event_type": event_type,
        "severity": severity,
        "site_id": "zahradky",
        "station_id": station_id,
        "camera_id": camera_id,
        "service_id": service_id,
        "created_at": utcnow_iso(),
        "risk_score": risk,
        "snapshot_url": None,
        "clip_url": None,
        "algorithm_version": "zahradky-fall-v1" if event_type.startswith("fall") else None,
        "payload_json": {"simulated": True},
    }
    r = client.post(f"{base}/api/v1/events", headers=headers(api_key), json=body)
    r.raise_for_status()
    print(f"event {event_type} id={eid} status={r.status_code}")
    return eid


def main() -> int:
    p = argparse.ArgumentParser(description="CableGuard Platform system simulator")
    p.add_argument("--base-url", default=DEFAULT_BASE)
    p.add_argument("--api-key", default=None, help="Defaults to CABLEGUARD_INGEST_API_KEY from env/.env")
    p.add_argument(
        "--scenario",
        choices=["demo", "heartbeats", "fall", "bar", "camera_offline", "io_fault", "idempotence", "stop_one"],
        default="demo",
    )
    p.add_argument("--stop-service", default="zahradky-horni-pad-detector")
    p.add_argument("--interval", type=float, default=2.0)
    p.add_argument("--rounds", type=int, default=3)
    args = p.parse_args()

    api_key = args.api_key
    kiosk_key = None
    if not api_key:
        import os
        from pathlib import Path

        from dotenv import load_dotenv

        load_dotenv(Path(__file__).resolve().parents[1] / ".env")
        api_key = os.environ.get("CABLEGUARD_INGEST_API_KEY", "change-me-local-dev-key")
        kiosk_key = os.environ.get("CABLEGUARD_KIOSK_API_KEY", "change-me-local-kiosk-key")
    else:
        import os

        kiosk_key = os.environ.get("CABLEGUARD_KIOSK_API_KEY", "change-me-local-kiosk-key")

    base = args.base_url.rstrip("/")
    with httpx.Client(timeout=10.0) as client:
        # health check
        h = client.get(f"{base}/api/v1/health")
        h.raise_for_status()

        if args.scenario in ("demo", "heartbeats"):
            for _ in range(args.rounds):
                for svc in SERVICES:
                    if args.scenario == "demo" and svc["service_id"] == args.stop_service and _ == args.rounds - 1:
                        print(f"skipping heartbeat for {args.stop_service} (simulate stop)")
                        continue
                    send_heartbeat(client, base, api_key, svc)
                time.sleep(args.interval)

        if args.scenario in ("demo", "fall"):
            send_event(client, base, api_key, "fall_risk_detected")

        if args.scenario == "bar":
            send_event(client, base, api_key, "safety_bar_alarm")

        if args.scenario == "camera_offline":
            send_event(client, base, api_key, "camera_offline")

        if args.scenario == "io_fault":
            send_event(client, base, api_key, "io_fault")

        if args.scenario == "idempotence":
            eid = str(uuid.uuid4())
            send_event(client, base, api_key, "fall_risk_detected", event_id=eid)
            send_event(client, base, api_key, "fall_risk_detected", event_id=eid)

        if args.scenario == "stop_one":
            for _ in range(args.rounds):
                for svc in SERVICES:
                    if svc["service_id"] == args.stop_service:
                        continue
                    send_heartbeat(client, base, api_key, svc)
                time.sleep(args.interval)
            print(f"stopped heartbeats for {args.stop_service}; wait for offline timeout")

        if args.scenario == "demo":
            # acknowledge last open fall if any
            ev = client.get(f"{base}/api/v1/events", params={"event_type": "fall_risk_detected", "status": "open"})
            items = ev.json().get("items", [])
            if items:
                eid = items[0]["event_id"]
                ar = client.post(
                    f"{base}/api/v1/events/{eid}/acknowledge",
                    headers=kiosk_headers(kiosk_key),
                    json={"acknowledged_by": "sim-operator", "kiosk_id": "sim-kiosk", "note": "demo ack"},
                )
                ar.raise_for_status()
                print(f"acknowledged {eid}")

        status = client.get(f"{base}/api/v1/status")
        print("status:", status.json())
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except httpx.HTTPError as e:
        print(f"HTTP error: {e}", file=sys.stderr)
        raise SystemExit(1)
