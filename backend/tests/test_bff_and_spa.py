from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
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


def test_bff_ack_without_browser_kiosk_key(
    client: TestClient, auth_headers: dict
) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    r = client.post(
        f"/bff/events/{eid}/acknowledge",
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a", "note": "ok"},
    )
    assert r.status_code == 200
    assert r.json()["event"]["status"] == "acknowledged"


def test_bff_ack_idempotent(client: TestClient, auth_headers: dict) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    body = {"acknowledged_by": "operator1", "kiosk_id": "kiosk-a", "note": "ok"}
    first = client.post(f"/bff/events/{eid}/acknowledge", json=body)
    second = client.post(f"/bff/events/{eid}/acknowledge", json=body)
    assert first.status_code == 200
    assert second.status_code == 200
    assert (
        first.json()["acknowledgement"]["acknowledgement_id"]
        == second.json()["acknowledgement"]["acknowledgement_id"]
    )


def test_bff_ack_conflict_409(client: TestClient, auth_headers: dict) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    client.post(
        f"/bff/events/{eid}/acknowledge",
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"},
    )
    r = client.post(
        f"/bff/events/{eid}/acknowledge",
        json={"acknowledged_by": "other", "kiosk_id": "kiosk-a"},
    )
    assert r.status_code == 409


def test_spa_static_fallback(tmp_path: Path, monkeypatch) -> None:
    ui = tmp_path / "ui"
    assets = ui / "assets"
    assets.mkdir(parents=True)
    (ui / "index.html").write_text("<!doctype html><title>CG</title>", encoding="utf-8")
    (assets / "app.js").write_text("console.log(1)", encoding="utf-8")

    monkeypatch.setenv("CABLEGUARD_ENV", "test")
    monkeypatch.setenv("CABLEGUARD_INGEST_API_KEY", "test-ingest-key")
    monkeypatch.setenv("CABLEGUARD_KIOSK_API_KEY", "test-kiosk-key")
    monkeypatch.setenv("CABLEGUARD_DATABASE_URL", f"sqlite:///{(tmp_path / 'db.sqlite3').as_posix()}")
    monkeypatch.setenv("CABLEGUARD_MONITOR_STATIC_DIR", str(ui))
    monkeypatch.setenv("CABLEGUARD_MONITOR_UI_UPSTREAM", "")

    from app.core.config import get_settings

    get_settings.cache_clear()

    from alembic import command
    from alembic.config import Config
    from app.db.session import init_engine
    from app.main import create_app

    settings = get_settings()
    init_engine(settings.database_url)
    backend_root = Path(__file__).resolve().parents[1]
    alembic_cfg = Config(str(backend_root / "alembic.ini"))
    alembic_cfg.set_main_option("script_location", str(backend_root / "migrations"))
    command.upgrade(alembic_cfg, "head")

    app = create_app()
    with TestClient(app) as c:
        root = c.get("/")
        assert root.status_code == 200
        assert "CG" in root.text

        deep = c.get("/kiosk/zahradky/horni-stanice")
        assert deep.status_code == 200
        assert "CG" in deep.text

        asset = c.get("/assets/app.js")
        assert asset.status_code == 200
        assert "console.log" in asset.text

        # API still works
        health = c.get("/api/v1/health")
        assert health.status_code == 200

    get_settings.cache_clear()
