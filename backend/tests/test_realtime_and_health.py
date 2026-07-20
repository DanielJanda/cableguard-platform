from __future__ import annotations

from datetime import datetime, timezone, timedelta
from pathlib import Path
from uuid import uuid4

from alembic import command
from alembic.config import Config
from fastapi.testclient import TestClient

from app.core.config import get_settings
from app.db import session as db_session
from app.db.session import init_engine
from app.main import create_app
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


def test_websocket_snapshot_and_event(client: TestClient, auth_headers: dict) -> None:
    with client.websocket_connect("/ws/v1") as ws:
        snap = ws.receive_json()
        assert snap["type"] == "system.snapshot"
        assert "services" in snap["data"]

        eid = str(uuid4())
        payload = {
            "event_id": eid,
            "event_type": "fall_risk_detected",
            "severity": "alarm",
            "site_id": "zahradky",
            "station_id": "horni_stanice",
            "camera_id": "kamera4",
            "service_id": "zahradky-horni-pad-detector",
            "created_at": datetime.now(timezone.utc).isoformat(),
            "risk_score": 0.8,
        }
        r = client.post("/api/v1/events", headers=auth_headers, json=payload)
        assert r.status_code == 201
        msg = ws.receive_json()
        assert msg["type"] == "event.created"
        assert msg["data"]["event_id"] == eid


def test_websocket_service_updated(client: TestClient, auth_headers: dict) -> None:
    with client.websocket_connect("/ws/v1") as ws:
        assert ws.receive_json()["type"] == "system.snapshot"
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
        msg = ws.receive_json()
        assert msg["type"] == "service.updated"
        assert msg["data"]["service_id"] == "zahradky-horni-pad-detector"


def test_restart_preserves_history(tmp_path: Path, monkeypatch) -> None:
    """History survives in SQLite file across app restarts (same DB path)."""
    db_path = tmp_path / "persist.sqlite3"
    monkeypatch.setenv("CABLEGUARD_INGEST_API_KEY", "test-ingest-key")
    monkeypatch.setenv("CABLEGUARD_HEARTBEAT_TIMEOUT_SEC", "6")
    monkeypatch.setenv("CABLEGUARD_DATABASE_URL", f"sqlite:///{db_path.as_posix()}")
    get_settings.cache_clear()

    backend_root = Path(__file__).resolve().parents[1]
    alembic_cfg = Config(str(backend_root / "alembic.ini"))
    alembic_cfg.set_main_option("script_location", str(backend_root / "migrations"))
    init_engine(get_settings().database_url)
    command.upgrade(alembic_cfg, "head")

    headers = {"X-API-Key": "test-ingest-key"}
    eid = str(uuid4())
    payload = {
        "event_id": eid,
        "event_type": "camera_offline",
        "severity": "warning",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_id": "mediamtx",
        "created_at": datetime.now(timezone.utc).isoformat(),
    }

    app1 = create_app()
    with TestClient(app1) as c1:
        assert c1.post("/api/v1/events", headers=headers, json=payload).status_code == 201
        assert c1.get("/api/v1/events").json()["total"] == 1

    get_settings.cache_clear()
    init_engine(f"sqlite:///{db_path.as_posix()}")
    app2 = create_app()
    with TestClient(app2) as c2:
        detail = c2.get(f"/api/v1/events/{eid}")
        assert detail.status_code == 200
        assert c2.get("/api/v1/events").json()["total"] == 1

    get_settings.cache_clear()
