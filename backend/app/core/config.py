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
    ingest_api_key: str = ""
    kiosk_api_key: str = ""
    heartbeat_timeout_sec: float = 6.0
    # Canonical public browser origin (monitor + MediaMTX webrtcAllowOrigin + kiosk).
    # Example: http://10.6.1.40:8080 or later http://cableguard.<firma-domena>
    public_origin: str = "http://10.6.1.40:8080"
    # Local Nitro node-server for production SSR UI (preferred). Empty = disabled.
    monitor_ui_upstream: str = ""
    # Optional prepared SPA directory (StaticFiles + index fallback). Used when upstream empty.
    monitor_static_dir: str = ""
    # Operator kiosk URL path under public_origin
    kiosk_path: str = "/kiosk/zahradky/horni-stanice"
    cors_origins: str = (
        "http://10.6.1.40:8080,http://localhost:8080,http://127.0.0.1:8080,"
        "http://127.0.0.1:5173,http://localhost:5173"
    )

    @property
    def cors_origins_list(self) -> list[str]:
        origins = [o.strip() for o in self.cors_origins.split(",") if o.strip()]
        pub = self.public_origin.strip().rstrip("/")
        if pub and pub not in origins:
            origins.insert(0, pub)
        return origins

    @property
    def kiosk_url(self) -> str:
        base = self.public_origin.strip().rstrip("/")
        path = self.kiosk_path if self.kiosk_path.startswith("/") else f"/{self.kiosk_path}"
        return f"{base}{path}"

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
