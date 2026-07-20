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
    monkeypatch.setenv("CABLEGUARD_HEARTBEAT_TIMEOUT_SEC", "1")
    monkeypatch.setenv("CABLEGUARD_DATABASE_URL", f"sqlite:///{db_path.as_posix()}")

    # Clear settings cache and re-import app pieces
    from app.core.config import get_settings

    get_settings.cache_clear()

    from app.db.session import init_engine
    from app.main import create_app
    from alembic import command
    from alembic.config import Config

    settings = get_settings()
    init_engine(settings.database_url)

    # Run alembic migrations against test DB
    backend_root = Path(__file__).resolve().parents[1]
    alembic_cfg = Config(str(backend_root / "alembic.ini"))
    alembic_cfg.set_main_option("script_location", str(backend_root / "migrations"))
    # env.py uses get_settings() which now points at test DB
    command.upgrade(alembic_cfg, "head")

    app = create_app()
    with TestClient(app) as c:
        yield c

    get_settings.cache_clear()


@pytest.fixture()
def auth_headers() -> dict[str, str]:
    return {"X-API-Key": "test-ingest-key"}
