from __future__ import annotations

from functools import lru_cache
from pathlib import Path

from pydantic_settings import BaseSettings, SettingsConfigDict

# Repo root: backend/app/core/config.py -> parents[3]
REPO_ROOT = Path(__file__).resolve().parents[3]


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=(
            str(REPO_ROOT / ".env"),
            ".env",
        ),
        env_file_encoding="utf-8",
        extra="ignore",
        env_prefix="CABLEGUARD_",
    )

    env: str = "development"
    host: str = "127.0.0.1"
    port: int = 8000
    database_url: str = f"sqlite:///{(REPO_ROOT / 'data' / 'cableguard.sqlite3').as_posix()}"
    ingest_api_key: str = "change-me-local-dev-key"
    heartbeat_timeout_sec: float = 6.0
    cors_origins: str = "http://127.0.0.1:5173,http://localhost:5173"

    @property
    def cors_origins_list(self) -> list[str]:
        return [o.strip() for o in self.cors_origins.split(",") if o.strip()]

    def resolve_sqlite_path(self) -> Path | None:
        url = self.database_url
        if url.startswith("sqlite:///"):
            raw = url.removeprefix("sqlite:///")
            path = Path(raw)
            if not path.is_absolute():
                path = (REPO_ROOT / path).resolve()
            return path
        return None


@lru_cache
def get_settings() -> Settings:
    return Settings()
