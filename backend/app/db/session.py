from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

from sqlalchemy import create_engine, event, text
from sqlalchemy.engine import Engine
from sqlalchemy.orm import Session, sessionmaker

_engine: Engine | None = None
SessionLocal: sessionmaker[Session] | None = None


def _sqlite_url_to_path(url: str) -> Path | None:
    if not url.startswith("sqlite:///"):
        return None
    raw = url.removeprefix("sqlite:///")
    return Path(raw)


def init_engine(database_url: str) -> Engine:
    global _engine, SessionLocal
    path = _sqlite_url_to_path(database_url)
    if path is not None:
        if not path.is_absolute():
            # relative to cwd; prefer repo data/
            from app.core.config import REPO_ROOT

            path = (REPO_ROOT / path).resolve()
            database_url = f"sqlite:///{path.as_posix()}"
        path.parent.mkdir(parents=True, exist_ok=True)

    connect_args = {"check_same_thread": False} if database_url.startswith("sqlite") else {}
    _engine = create_engine(database_url, future=True, connect_args=connect_args)

    if database_url.startswith("sqlite"):

        @event.listens_for(_engine, "connect")
        def _set_sqlite_pragma(dbapi_conn, _connection_record) -> None:  # type: ignore[no-untyped-def]
            cursor = dbapi_conn.cursor()
            cursor.execute("PRAGMA journal_mode=WAL")
            cursor.execute("PRAGMA foreign_keys=ON")
            cursor.close()

    SessionLocal = sessionmaker(bind=_engine, autoflush=False, autocommit=False, future=True)
    return _engine


def get_engine() -> Engine:
    if _engine is None:
        raise RuntimeError("Database engine not initialized")
    return _engine


def get_session_factory() -> sessionmaker[Session]:
    """Return the live session factory (always read via this helper after init)."""
    if SessionLocal is None:
        raise RuntimeError("Database session factory not initialized")
    return SessionLocal


def get_db() -> Generator[Session, None, None]:
    db = get_session_factory()()
    try:
        yield db
    finally:
        db.close()
