"""Load and validate the committed camera registry (no secrets)."""

from __future__ import annotations

import re
from functools import lru_cache
from pathlib import Path
from typing import Any

import yaml
from pydantic import BaseModel, Field, field_validator

from app.core.config import REPO_ROOT

REGISTRY_PATH = REPO_ROOT / "deploy" / "cameras" / "registry.yml"

_CREDENTIAL_PATTERN = re.compile(r"://[^/@]+:[^/@]+@")


class CameraRegistryEntry(BaseModel):
    camera_id: str
    site_id: str
    station_id: str
    stream_name: str
    rtsp_proxy_path: str
    whep_path: str
    enabled: bool = True

    @field_validator("rtsp_proxy_path")
    @classmethod
    def validate_rtsp_proxy(cls, value: str) -> str:
        if _CREDENTIAL_PATTERN.search(value):
            raise ValueError("rtsp_proxy_path must not contain credentials")
        if not value.startswith("rtsp://127.0.0.1:"):
            raise ValueError("rtsp_proxy_path must use local MediaMTX proxy host")
        return value

    @field_validator("whep_path")
    @classmethod
    def validate_whep_path(cls, value: str) -> str:
        if "@" in value or "://" in value:
            raise ValueError("whep_path must be a relative WHEP path")
        if not value.startswith("/") or not value.endswith("/whep"):
            raise ValueError("whep_path must look like /stream-name/whep")
        return value


class CameraRegistry(BaseModel):
    registry_version: int = 1
    cameras: list[CameraRegistryEntry] = Field(default_factory=list)


def load_registry(path: Path | None = None) -> CameraRegistry:
    cfg_path = path or REGISTRY_PATH
    if not cfg_path.is_file():
        raise FileNotFoundError(f"Camera registry not found: {cfg_path}")
    with open(cfg_path, encoding="utf-8") as fh:
        raw = yaml.safe_load(fh)
    if not isinstance(raw, dict):
        raise ValueError(f"Invalid registry format: {cfg_path}")
    _scan_forbidden_secrets(raw)
    return CameraRegistry.model_validate(raw)


def _scan_forbidden_secrets(raw: dict[str, Any]) -> None:
    blob = yaml.safe_dump(raw, allow_unicode=True)
    if _CREDENTIAL_PATTERN.search(blob):
        raise ValueError("Camera registry must not contain RTSP credentials")
    for token in ("password", "PASSWORD", "USERNAME", "secret", "token"):
        if token in blob:
            raise ValueError(f"Camera registry must not contain secret marker '{token}'")


@lru_cache
def get_registry() -> CameraRegistry:
    return load_registry()


def get_camera(camera_id: str) -> CameraRegistryEntry | None:
    for camera in get_registry().cameras:
        if camera.camera_id == camera_id:
            return camera
    return None


def list_enabled_cameras() -> list[CameraRegistryEntry]:
    return [c for c in get_registry().cameras if c.enabled]
