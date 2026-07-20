from __future__ import annotations

from datetime import datetime, timezone
from uuid import uuid4

from fastapi.testclient import TestClient


def _event_payload(event_id: str | None = None) -> dict:
    return {
        "event_id": event_id or str(uuid4()),
        "event_type": "fall_risk_detected",
        "severity": "alarm",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "camera_id": "kamera4",
        "service_id": "zahradky-horni-pad-detector",
        "created_at": datetime.now(timezone.utc).isoformat(),
        "risk_score": 0.75,
        "algorithm_version": "zahradky-fall-v1",
        "model_sha256": "ABC",
        "config_sha256": "DEF",
        "payload_json": {"track_id": 3},
    }


def test_health(client: TestClient) -> None:
    r = client.get("/api/v1/health")
    assert r.status_code == 200
    assert r.json()["ok"] is True


def test_ingest_requires_api_key(client: TestClient) -> None:
    r = client.post("/api/v1/events", json=_event_payload())
    assert r.status_code == 401


def test_create_event_and_idempotency(client: TestClient, auth_headers: dict) -> None:
    eid = str(uuid4())
    r1 = client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    assert r1.status_code == 201
    r2 = client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    assert r2.status_code == 200
    listed = client.get("/api/v1/events")
    assert listed.status_code == 200
    assert listed.json()["total"] == 1


def test_heartbeat_upsert_and_history(client: TestClient, auth_headers: dict) -> None:
    hb = {
        "service_id": "zahradky-horni-pad-detector",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_type": "fall_detector",
        "status": "healthy",
        "camera_connected": True,
        "inference_running": True,
    }
    r = client.post("/api/v1/heartbeats", headers=auth_headers, json=hb)
    assert r.status_code == 200
    assert r.json()["status"] == "healthy"

    hb["status"] = "degraded"
    r2 = client.post("/api/v1/heartbeats", headers=auth_headers, json=hb)
    assert r2.status_code == 200
    assert r2.json()["status"] == "degraded"

    hist = client.get("/api/v1/status/history", params={"service_id": "zahradky-horni-pad-detector"})
    assert hist.status_code == 200
    assert len(hist.json()) >= 2


def test_acknowledge(client: TestClient, auth_headers: dict) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    r = client.post(
        f"/api/v1/events/{eid}/acknowledge",
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a", "note": "ok"},
    )
    assert r.status_code == 200
    body = r.json()
    assert body["event"]["status"] == "acknowledged"
    assert body["acknowledgement"]["kiosk_id"] == "kiosk-a"


def test_event_filters(client: TestClient, auth_headers: dict) -> None:
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload())
    other = _event_payload()
    other["event_type"] = "safety_bar_alarm"
    other["station_id"] = "dolni_stanice"
    client.post("/api/v1/events", headers=auth_headers, json=other)
    r = client.get("/api/v1/events", params={"event_type": "safety_bar_alarm"})
    assert r.json()["total"] == 1
