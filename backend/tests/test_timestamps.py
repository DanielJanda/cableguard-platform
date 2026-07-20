from __future__ import annotations

from datetime import datetime, timezone
from uuid import uuid4

from fastapi.testclient import TestClient


def test_rest_timestamps_are_timezone_aware_utc(client: TestClient, auth_headers: dict) -> None:
    eid = str(uuid4())
    payload = {
        "event_id": eid,
        "event_type": "fall_risk_detected",
        "severity": "alarm",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_id": "zahradky-horni-pad-detector",
        "created_at": datetime.now(timezone.utc).isoformat(),
        "risk_score": 0.5,
    }
    created = client.post("/api/v1/events", headers=auth_headers, json=payload)
    assert created.status_code == 201
    body = created.json()
    assert body["created_at"].endswith("+00:00")
    assert body["received_at"].endswith("+00:00")


def test_naive_created_at_is_normalized_to_utc(client: TestClient, auth_headers: dict) -> None:
    payload = {
        "event_id": str(uuid4()),
        "event_type": "fall_risk_detected",
        "severity": "alarm",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_id": "zahradky-horni-pad-detector",
        "created_at": "2026-07-20T12:00:00",
        "risk_score": 0.5,
    }
    r = client.post("/api/v1/events", headers=auth_headers, json=payload)
    assert r.status_code == 201
    assert r.json()["created_at"].endswith("+00:00")


def test_status_history_timestamps_are_utc(client: TestClient, auth_headers: dict) -> None:
    hb = {
        "service_id": "zahradky-io-agent",
        "site_id": "zahradky",
        "service_type": "io_agent",
        "status": "healthy",
    }
    client.post("/api/v1/heartbeats", headers=auth_headers, json=hb)
    hist = client.get("/api/v1/status/history", params={"service_id": "zahradky-io-agent"}).json()
    assert hist
    assert hist[0]["changed_at"].endswith("+00:00")
