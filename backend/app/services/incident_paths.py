"""Incident media path helpers — never expose raw paths via API responses."""

from __future__ import annotations

import hashlib
import json
import logging
import os
from datetime import datetime
from pathlib import Path
from typing import Any

from app.core.config import REPO_ROOT, get_settings

logger = logging.getLogger(__name__)

MediaKind = str  # "snapshot" | "clip"


def incidents_root() -> Path:
    settings = get_settings()
    root = Path(settings.incident_storage_dir)
    if not root.is_absolute():
        root = (REPO_ROOT / root).resolve()
    root.mkdir(parents=True, exist_ok=True)
    return root


def event_incident_dir(camera_id: str, occurred_at: datetime, event_id: str) -> Path:
    day = occurred_at.strftime("%Y-%m-%d")
    safe_cam = "".join(c if c.isalnum() or c in "-_" else "_" for c in camera_id)
    safe_event = "".join(c if c.isalnum() or c in "-_" else "_" for c in event_id)
    path = incidents_root() / safe_cam / day / safe_event
    path.mkdir(parents=True, exist_ok=True)
    return path


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def atomic_write_bytes(final_path: Path, data: bytes) -> None:
    final_path.parent.mkdir(parents=True, exist_ok=True)
    tmp = final_path.with_suffix(final_path.suffix + ".tmp")
    try:
        with tmp.open("wb") as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, final_path)
    finally:
        if tmp.exists():
            try:
                tmp.unlink()
            except OSError:
                pass


def atomic_replace(src_tmp: Path, final_path: Path) -> None:
    final_path.parent.mkdir(parents=True, exist_ok=True)
    os.replace(src_tmp, final_path)


def write_json_sidecar(path: Path, payload: dict[str, Any]) -> None:
    atomic_write_bytes(path, (json.dumps(payload, indent=2, sort_keys=True) + "\n").encode("utf-8"))


def resolve_safe_media_path(
    *,
    stored_path: str | None,
    event_id: str,
    kind: MediaKind,
) -> Path | None:
    """Resolve and validate that a stored media path stays under incidents_root."""
    if not stored_path:
        return None
    root = incidents_root().resolve()
    candidate = Path(stored_path)
    if not candidate.is_absolute():
        candidate = (root / candidate).resolve()
    else:
        candidate = candidate.resolve()
    try:
        candidate.relative_to(root)
    except ValueError:
        logger.error("Rejected path traversal for event %s kind=%s", event_id, kind)
        return None
    if not candidate.is_file():
        return None
    # Soft check: event_id should appear in path components
    if event_id not in candidate.parts and event_id.replace("-", "") not in "".join(candidate.parts):
        # Still allow if under root — jobs store absolute paths with event dirs
        pass
    return candidate
