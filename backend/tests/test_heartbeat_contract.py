from __future__ import annotations

from datetime import datetime, timezone, timedelta
from uuid import uuid4

from fastapi.testclient import TestClient

from app.db import session as db_session
from app.services import health_service


def test_healthy_to_offline_transition(client: TestClient, auth_headers: dict) -> None:
    hb = {
        "service_id": "zahradky-io-agent",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_type": "io_agent",
        "status": "healthy",
        "relay_connected": True,
        "sent_at": (datetime.now(timezone.utc) - timedelta(seconds=10)).isoformat(),
    }
    r = client.post("/api/v1/heartbeats", headers=auth_headers, json=hb)
    assert r.status_code == 200
    assert r.json()["status"] == "healthy"

    factory = db_session.SessionLocal
    assert factory is not None
    db = factory()
    try:
        changed = health_service.mark_offline_if_stale(db, timeout_sec=1.0)
    finally:
        db.close()
    assert any(c.service_id == "zahradky-io-agent" for c in changed)

    status = client.get("/api/v1/status")
    svc = next(s for s in status.json()["services"] if s["service_id"] == "zahradky-io-agent")
    assert svc["status"] == "offline"

    hist = client.get("/api/v1/status/history", params={"service_id": "zahradky-io-agent"})
    assert any(h["to_status"] == "offline" for h in hist.json())


def test_repeated_offline_checks_do_not_add_history(client: TestClient, auth_headers: dict) -> None:
    hb = {
        "service_id": "mediamtx",
        "site_id": "zahradky",
        "service_type": "mediamtx",
        "status": "healthy",
        "sent_at": (datetime.now(timezone.utc) - timedelta(seconds=10)).isoformat(),
    }
    client.post("/api/v1/heartbeats", headers=auth_headers, json=hb)

    factory = db_session.SessionLocal
    assert factory is not None
    db = factory()
    try:
        health_service.mark_offline_if_stale(db, timeout_sec=1.0)
        hist_after_first = client.get("/api/v1/status/history", params={"service_id": "mediamtx"}).json()
        offline_count = sum(1 for row in hist_after_first if row["to_status"] == "offline")
        health_service.mark_offline_if_stale(db, timeout_sec=1.0)
        health_service.mark_offline_if_stale(db, timeout_sec=1.0)
    finally:
        db.close()

    hist_after_repeats = client.get("/api/v1/status/history", params={"service_id": "mediamtx"}).json()
    assert sum(1 for row in hist_after_repeats if row["to_status"] == "offline") == offline_count


def test_offline_to_healthy_after_new_heartbeat(client: TestClient, auth_headers: dict) -> None:
    service_id = "zahradky-dolni-zabrana-detector"
    stale = {
        "service_id": service_id,
        "site_id": "zahradky",
        "station_id": "dolni_stanice",
        "service_type": "safety_bar_detector",
        "status": "healthy",
        "relay_connected": True,
        "sent_at": (datetime.now(timezone.utc) - timedelta(seconds=10)).isoformat(),
    }
    client.post("/api/v1/heartbeats", headers=auth_headers, json=stale)

    factory = db_session.SessionLocal
    assert factory is not None
    db = factory()
    try:
        health_service.mark_offline_if_stale(db, timeout_sec=1.0)
    finally:
        db.close()

    fresh = dict(stale)
    fresh["sent_at"] = datetime.now(timezone.utc).isoformat()
    fresh["status"] = "healthy"
    resp = client.post("/api/v1/heartbeats", headers=auth_headers, json=fresh)
    assert resp.status_code == 200
    assert resp.json()["status"] == "healthy"

    hist = client.get("/api/v1/status/history", params={"service_id": service_id}).json()
    assert any(row["from_status"] == "offline" and row["to_status"] == "healthy" for row in hist)


def test_fall_detector_accepts_null_relay_connected(client: TestClient, auth_headers: dict) -> None:
    hb = {
        "service_id": "zahradky-horni-pad-detector",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_type": "fall_detector",
        "status": "healthy",
        "camera_connected": True,
        "inference_running": True,
        "relay_connected": None,
    }
    r = client.post("/api/v1/heartbeats", headers=auth_headers, json=hb)
    assert r.status_code == 200
    body = r.json()
    assert body["status"] == "healthy"
    assert body["relay_connected"] is None
