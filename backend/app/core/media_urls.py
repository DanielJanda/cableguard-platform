"""Validation for snapshot_url and clip_url ingest fields."""

from __future__ import annotations

import re

_WINDOWS_ABS = re.compile(r"^[A-Za-z]:[\\/]")
_UNC = re.compile(r"^\\\\")


def is_forbidden_media_reference(value: str) -> bool:
    stripped = value.strip()
    if not stripped:
        return False
    lower = stripped.lower()
    if lower.startswith("file://"):
        return True
    if _UNC.match(stripped):
        return True
    if _WINDOWS_ABS.match(stripped):
        return True
    # Unix absolute path (not allowed except future /api/ relative URLs)
    if stripped.startswith("/") and not stripped.startswith("/api/"):
        return True
    return False


def validate_media_url(value: str | None) -> str | None:
    if value is None:
        return None
    stripped = value.strip()
    if not stripped:
        return None
    if is_forbidden_media_reference(stripped):
        raise ValueError(
            "Media URL must not reference local filesystem paths (Windows, UNC, file://, or absolute paths)"
        )
    lower = stripped.lower()
    if stripped.startswith("/api/"):
        return stripped
    if lower.startswith("http://") or lower.startswith("https://"):
        return stripped
    raise ValueError("Media URL must be null, start with /api/, or be an http(s):// URL")
