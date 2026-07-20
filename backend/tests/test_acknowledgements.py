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
        "payload_json": {"track_id": 3},
    }


def test_ack_requires_kiosk_key(client: TestClient, auth_headers: dict) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    r = client.post(
        f"/api/v1/events/{eid}/acknowledge",
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"},
    )
    assert r.status_code == 401


def test_ack_rejects_wrong_kiosk_key(client: TestClient, auth_headers: dict) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    r = client.post(
        f"/api/v1/events/{eid}/acknowledge",
        headers={"X-Kiosk-Key": "wrong-kiosk-key"},
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"},
    )
    assert r.status_code == 401


def test_first_acknowledgement(
    client: TestClient, auth_headers: dict, kiosk_headers: dict
) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    r = client.post(
        f"/api/v1/events/{eid}/acknowledge",
        headers=kiosk_headers,
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a", "note": "checked"},
    )
    assert r.status_code == 200
    assert r.json()["event"]["status"] == "acknowledged"


def test_identical_ack_repeat_is_idempotent(
    client: TestClient, auth_headers: dict, kiosk_headers: dict
) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    body = {"acknowledged_by": "operator1", "kiosk_id": "kiosk-a", "note": "checked"}
    first = client.post(f"/api/v1/events/{eid}/acknowledge", headers=kiosk_headers, json=body)
    second = client.post(f"/api/v1/events/{eid}/acknowledge", headers=kiosk_headers, json=body)
    assert first.status_code == 200
    assert second.status_code == 200
    assert (
        first.json()["acknowledgement"]["acknowledgement_id"]
        == second.json()["acknowledgement"]["acknowledgement_id"]
    )


def test_conflicting_second_acknowledgement_returns_409(
    client: TestClient, auth_headers: dict, kiosk_headers: dict
) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    first = client.post(
        f"/api/v1/events/{eid}/acknowledge",
        headers=kiosk_headers,
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"},
    )
    assert first.status_code == 200
    conflict = client.post(
        f"/api/v1/events/{eid}/acknowledge",
        headers=kiosk_headers,
        json={"acknowledged_by": "operator2", "kiosk_id": "kiosk-b"},
    )
    assert conflict.status_code == 409


def test_acknowledge_missing_event(client: TestClient, kiosk_headers: dict) -> None:
    r = client.post(
        f"/api/v1/events/{uuid4()}/acknowledge",
        headers=kiosk_headers,
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"},
    )
    assert r.status_code == 404
