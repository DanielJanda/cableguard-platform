from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import AsyncMock, patch
from uuid import uuid4

from alembic import command
from alembic.config import Config
from fastapi.testclient import TestClient

from app.core.config import get_settings
from app.db import session as db_session
from app.db.session import init_engine
from app.main import create_app
from app.services import health_service


def test_websocket_snapshot_and_event(client: TestClient, auth_headers: dict) -> None:
    with client.websocket_connect("/ws/v1") as ws:
        snap = ws.receive_json()
        assert snap["type"] == "system.snapshot"
        assert snap["sent_at"].endswith("+00:00")
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
            "algorithm_version": "zahradky-fall-v1",
            "model_sha256": "ABC",
            "config_sha256": "DEF",
        }
        r = client.post("/api/v1/events", headers=auth_headers, json=payload)
        assert r.status_code == 201
        msg = ws.receive_json()
        assert msg["type"] == "event.created"
        assert msg["data"]["event_id"] == eid
        assert msg["data"]["received_at"].endswith("+00:00")
        assert msg["data"]["snapshot_url"] is None
        assert msg["data"]["clip_url"] is None
        assert "payload_json" not in msg["data"]


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
        assert msg["data"]["last_heartbeat_at"].endswith("+00:00")


def test_two_websocket_clients_receive_event_created(client: TestClient, auth_headers: dict) -> None:
    with client.websocket_connect("/ws/v1") as ws1, client.websocket_connect("/ws/v1") as ws2:
        assert ws1.receive_json()["type"] == "system.snapshot"
        assert ws2.receive_json()["type"] == "system.snapshot"
        eid = str(uuid4())
        payload = {
            "event_id": eid,
            "event_type": "fall_risk_detected",
            "severity": "alarm",
            "site_id": "zahradky",
            "station_id": "horni_stanice",
            "service_id": "zahradky-horni-pad-detector",
            "created_at": datetime.now(timezone.utc).isoformat(),
        }
        assert client.post("/api/v1/events", headers=auth_headers, json=payload).status_code == 201
        msg1 = ws1.receive_json()
        msg2 = ws2.receive_json()
        assert msg1["type"] == "event.created"
        assert msg2["type"] == "event.created"
        assert msg1["data"]["event_id"] == eid
        assert msg2["data"]["event_id"] == eid


def test_disconnected_websocket_client_does_not_break_rest_ingest(
    client: TestClient, auth_headers: dict
) -> None:
    with client.websocket_connect("/ws/v1") as ws:
        ws.receive_json()
    payload = {
        "event_id": str(uuid4()),
        "event_type": "io_fault",
        "severity": "critical",
        "site_id": "zahradky",
        "station_id": "horni_stanice",
        "service_id": "zahradky-io-agent",
        "created_at": datetime.now(timezone.utc).isoformat(),
    }
    r = client.post("/api/v1/events", headers=auth_headers, json=payload)
    assert r.status_code == 201


def test_new_websocket_client_receives_snapshot(client: TestClient, auth_headers: dict) -> None:
    client.post(
        "/api/v1/heartbeats",
        headers=auth_headers,
        json={
            "service_id": "zahradky-io-agent",
            "site_id": "zahradky",
            "service_type": "io_agent",
            "status": "healthy",
        },
    )
    with client.websocket_connect("/ws/v1") as ws:
        snap = ws.receive_json()
        assert snap["type"] == "system.snapshot"
        assert any(s["service_id"] == "zahradky-io-agent" for s in snap["data"]["services"])


def test_idempotent_ack_sends_single_websocket_message(
    client: TestClient, auth_headers: dict, kiosk_headers: dict
) -> None:
    eid = str(uuid4())
    client.post(
        "/api/v1/events",
        headers=auth_headers,
        json={
            "event_id": eid,
            "event_type": "fall_risk_detected",
            "severity": "alarm",
            "site_id": "zahradky",
            "station_id": "horni_stanice",
            "service_id": "zahradky-horni-pad-detector",
            "created_at": datetime.now(timezone.utc).isoformat(),
        },
    )
    ack_body = {"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"}
    with patch(
        "app.services.event_service.ws_manager.broadcast",
        new_callable=AsyncMock,
    ) as broadcast_mock:
        first = client.post(
            f"/api/v1/events/{eid}/acknowledge", headers=kiosk_headers, json=ack_body
        )
        second = client.post(
            f"/api/v1/events/{eid}/acknowledge", headers=kiosk_headers, json=ack_body
        )
        assert first.status_code == 200
        assert second.status_code == 200
        assert broadcast_mock.await_count == 1


def test_restart_preserves_history(tmp_path: Path, monkeypatch) -> None:
    """History survives in SQLite file across app restarts (same DB path)."""
    db_path = tmp_path / "persist.sqlite3"
    monkeypatch.setenv("CABLEGUARD_INGEST_API_KEY", "test-ingest-key")
    monkeypatch.setenv("CABLEGUARD_KIOSK_API_KEY", "test-kiosk-key")
    monkeypatch.setenv("CABLEGUARD_HEARTBEAT_TIMEOUT_SEC", "6")
    monkeypatch.setenv("CABLEGUARD_DATABASE_URL", f"sqlite:///{db_path.as_posix()}")
    get_settings.cache_clear()

    backend_root = Path(__file__).resolve().parents[1]
    alembic_cfg = Config(str(backend_root / "alembic.ini"))
    alembic_cfg.set_main_option("script_location", str(backend_root / "migrations"))
    init_engine(get_settings().database_url)
    command.upgrade(alembic_cfg, "head")

    headers = {"X-API-Key": "test-ingest-key"}
    kiosk = {"X-Kiosk-Key": "test-kiosk-key"}
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
        assert (
            c1.post(
                f"/api/v1/events/{eid}/acknowledge",
                headers=kiosk,
                json={"acknowledged_by": "op", "kiosk_id": "k1"},
            ).status_code
            == 200
        )

    get_settings.cache_clear()
    init_engine(f"sqlite:///{db_path.as_posix()}")
    app2 = create_app()
    with TestClient(app2) as c2:
        detail = c2.get(f"/api/v1/events/{eid}")
        assert detail.status_code == 200
        assert detail.json()["status"] == "acknowledged"
        assert c2.get("/api/v1/events").json()["total"] == 1

    get_settings.cache_clear()
