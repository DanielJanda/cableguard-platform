"""Office E2E: inject fall event and wait for MediaMTX incident clip."""

from __future__ import annotations

import json
import sqlite3
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path
from uuid import uuid4

ROOT = Path(__file__).resolve().parents[1]


def load_env() -> dict[str, str]:
    env: dict[str, str] = {}
    for line in (ROOT / ".env").read_text(encoding="utf-8").splitlines():
        if "=" in line and not line.strip().startswith("#"):
            k, v = line.split("=", 1)
            env[k.strip()] = v.strip()
    return env


def main() -> None:
    env = load_env()
    key = env["CABLEGUARD_INGEST_API_KEY"]
    event_id = str(uuid4())
    occurred = datetime.now(timezone.utc) - timedelta(seconds=40)
    body = {
        "event_id": event_id,
        "event_type": "fall_risk_detected",
        "severity": "critical",
        "site_id": "office",
        "station_id": "office-test",
        "camera_id": "office-63",
        "service_id": "fall-office-test",
        "created_at": occurred.isoformat().replace("+00:00", "Z"),
        "risk_score": 0.93,
        "payload_json": {
            "test_mode": True,
            "fall_episode_id": f"e2e-{event_id}",
            "pipeline_id": "fall-office-test",
            "track_id": "99",
            "frame_seq": 12345,
            "detector_type": "fall",
        },
    }
    req = urllib.request.Request(
        "http://127.0.0.1:8000/api/v1/events",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json", "X-API-Key": key},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        print("POST", resp.status, resp.read().decode()[:800])

    ev: dict = {}
    for i in range(45):
        time.sleep(2)
        with urllib.request.urlopen(
            f"http://127.0.0.1:8000/api/v1/events/{event_id}", timeout=15
        ) as resp:
            ev = json.loads(resp.read().decode())
        print(
            f"t={i * 2}s snap={ev.get('snapshot_status')} clip={ev.get('clip_status')}"
        )
        if ev.get("clip_status") in ("READY", "FAILED"):
            break

    print("FINAL", json.dumps(ev, indent=2)[:1500])
    for kind in ("snapshot", "clip"):
        try:
            r = urllib.request.Request(
                f"http://127.0.0.1:8000/api/v1/events/{event_id}/{kind}"
            )
            if kind == "clip":
                r.add_header("Range", "bytes=0-1023")
            with urllib.request.urlopen(r, timeout=60) as resp:
                data = resp.read()
                print(
                    kind,
                    "status",
                    getattr(resp, "status", 200),
                    "len",
                    len(data),
                    "ctype",
                    resp.headers.get("Content-Type"),
                    "cr",
                    resp.headers.get("Content-Range"),
                )
        except urllib.error.HTTPError as e:
            print(kind, "HTTP", e.code, e.read()[:200])
        except Exception as e:  # noqa: BLE001
            print(kind, "ERR", e)

    con = sqlite3.connect(str(ROOT / "data" / "cableguard.sqlite3"))
    row = con.execute(
        "select status,attempts,last_error,actual_duration_sec,final_path "
        "from incident_clip_jobs where event_id=?",
        (event_id,),
    ).fetchone()
    print("JOB", row)
    print("EVENT_ID", event_id)


if __name__ == "__main__":
    main()
