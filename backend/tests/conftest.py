from __future__ import annotations

import os
from collections.abc import Generator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient


@pytest.fixture()
def client(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Generator[TestClient, None, None]:
    db_path = tmp_path / "test.sqlite3"
    monkeypatch.setenv("CABLEGUARD_INGEST_API_KEY", "test-ingest-key")
    monkeypatch.setenv("CABLEGUARD_KIOSK_API_KEY", "test-kiosk-key")
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

    backend_root = Path(__file__).resolve().parents[1]
    alembic_cfg = Config(str(backend_root / "alembic.ini"))
    alembic_cfg.set_main_option("script_location", str(backend_root / "migrations"))
    command.upgrade(alembic_cfg, "head")

    app = create_app()
    with TestClient(app) as c:
        yield c

    get_settings.cache_clear()


@pytest.fixture()
def auth_headers() -> dict[str, str]:
    return {"X-API-Key": "test-ingest-key"}


@pytest.fixture()
def kiosk_headers() -> dict[str, str]:
    return {"X-Kiosk-Key": "test-kiosk-key"}


@pytest.fixture()
def auth_and_kiosk_headers(auth_headers: dict[str, str], kiosk_headers: dict[str, str]) -> dict[str, str]:
    return {**auth_headers, **kiosk_headers}
