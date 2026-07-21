from __future__ import annotations

from datetime import datetime, timezone
from uuid import uuid4

import pytest
from fastapi.testclient import TestClient

from app.core.config import Settings
from app.core.secrets import PLACEHOLDER_SECRETS, is_configured_secret, validate_runtime_secrets


def test_placeholder_secrets_are_rejected() -> None:
    for value in PLACEHOLDER_SECRETS:
        assert not is_configured_secret(value)


def test_explicit_test_secrets_are_accepted() -> None:
    assert is_configured_secret("test-ingest-key")
    assert is_configured_secret("test-kiosk-key")


def test_validate_runtime_secrets_rejects_placeholders() -> None:
    settings = Settings(
        ingest_api_key="change-me-local-dev-key",
        kiosk_api_key="change-me-local-kiosk-key",
    )
    with pytest.raises(RuntimeError, match="CABLEGUARD_INGEST_API_KEY"):
        validate_runtime_secrets(settings)


def test_validate_runtime_secrets_accepts_explicit_values() -> None:
    settings = Settings(
        ingest_api_key="local-dev-ingest-secret",
        kiosk_api_key="local-dev-kiosk-secret",
    )
    validate_runtime_secrets(settings)


@pytest.fixture()
def client_unconfigured_kiosk(
    tmp_path, monkeypatch: pytest.MonkeyPatch
) -> TestClient:
    db_path = tmp_path / "test.sqlite3"
    monkeypatch.setenv("CABLEGUARD_ENV", "test")
    monkeypatch.setenv("CABLEGUARD_INGEST_API_KEY", "test-ingest-key")
    monkeypatch.setenv("CABLEGUARD_KIOSK_API_KEY", "")
    monkeypatch.setenv("CABLEGUARD_HEARTBEAT_TIMEOUT_SEC", "1")
    monkeypatch.setenv("CABLEGUARD_DATABASE_URL", f"sqlite:///{db_path.as_posix()}")

    from app.core.config import get_settings

    get_settings.cache_clear()

    from alembic import command
    from alembic.config import Config
    from app.db.session import init_engine
    from app.main import create_app

    settings = get_settings()
    init_engine(settings.database_url)

    backend_root = __import__("pathlib").Path(__file__).resolve().parents[1]
    alembic_cfg = Config(str(backend_root / "alembic.ini"))
    alembic_cfg.set_main_option("script_location", str(backend_root / "migrations"))
    command.upgrade(alembic_cfg, "head")

    app = create_app()
    with TestClient(app) as c:
        yield c

    get_settings.cache_clear()


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


def test_ack_blocked_when_kiosk_key_not_configured(
    client_unconfigured_kiosk: TestClient,
) -> None:
    auth = {"X-API-Key": "test-ingest-key"}
    kiosk = {"X-Kiosk-Key": "test-kiosk-key"}
    eid = str(uuid4())
    client_unconfigured_kiosk.post(
        "/api/v1/events", headers=auth, json=_event_payload(eid)
    )
    response = client_unconfigured_kiosk.post(
        f"/api/v1/events/{eid}/acknowledge",
        headers=kiosk,
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"},
    )
    assert response.status_code == 503
    assert "Kiosk API key is not configured" in response.json()["detail"]


def test_ack_succeeds_with_explicit_kiosk_key(
    client: TestClient, auth_headers: dict, kiosk_headers: dict
) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    response = client.post(
        f"/api/v1/events/{eid}/acknowledge",
        headers=kiosk_headers,
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"},
    )
    assert response.status_code == 200


def test_ack_rejects_wrong_kiosk_key(
    client: TestClient, auth_headers: dict
) -> None:
    eid = str(uuid4())
    client.post("/api/v1/events", headers=auth_headers, json=_event_payload(eid))
    response = client.post(
        f"/api/v1/events/{eid}/acknowledge",
        headers={"X-Kiosk-Key": "wrong-kiosk-key"},
        json={"acknowledged_by": "operator1", "kiosk_id": "kiosk-a"},
    )
    assert response.status_code == 401
