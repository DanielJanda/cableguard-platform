from __future__ import annotations

from datetime import datetime, timezone
from uuid import uuid4

from fastapi.testclient import TestClient


def _payload(*, event_id: str, test_mode: bool | None = None, site_id: str = "zahradky", station_id: str = "horni-stanice") -> dict:
    body = {
        "event_id": event_id,
        "event_type": "fall_risk_detected",
        "severity": "critical",
        "site_id": site_id,
        "station_id": station_id,
        "camera_id": "cam-1",
        "service_id": "fall-zahradky-upper" if site_id != "office" else "fall-office-test",
        "created_at": datetime.now(timezone.utc).isoformat(),
        "risk_score": 0.9,
    }
    if test_mode is not None:
        body["payload_json"] = {"test_mode": test_mode}
    return body


def test_include_test_events_default_excludes_test(client: TestClient, auth_headers: dict) -> None:
    prod = _payload(event_id=str(uuid4()), test_mode=False)
    office = _payload(
        event_id=str(uuid4()),
        test_mode=True,
        site_id="office",
        station_id="office-test",
    )
    assert client.post("/api/v1/events", headers=auth_headers, json=prod).status_code == 201
    assert client.post("/api/v1/events", headers=auth_headers, json=office).status_code == 201

    default = client.get("/api/v1/events").json()
    assert default["total"] == 1
    assert default["items"][0]["payload_json"]["test_mode"] is False
    assert default["items"][0]["is_test"] is False

    with_test = client.get("/api/v1/events", params={"include_test_events": True}).json()
    assert with_test["total"] == 2
    flags = sorted(item["is_test"] for item in with_test["items"])
    assert flags == [False, True]


def test_office_filter_with_include_test(client: TestClient, auth_headers: dict) -> None:
    office_id = str(uuid4())
    assert (
        client.post(
            "/api/v1/events",
            headers=auth_headers,
            json=_payload(
                event_id=office_id,
                test_mode=True,
                site_id="office",
                station_id="office-test",
            ),
        ).status_code
        == 201
    )
    r = client.get(
        "/api/v1/events",
        params={
            "site_id": "office",
            "station_id": "office-test",
            "include_test_events": True,
        },
    ).json()
    assert r["total"] == 1
    assert r["items"][0]["is_test"] is True
    assert r["items"][0]["event_id"] == office_id

    # Production default view must not see office test event
    prod_view = client.get("/api/v1/events", params={"include_test_events": False}).json()
    assert all(not item["is_test"] for item in prod_view["items"])
