"""UTC datetime normalization and ISO 8601 serialization."""

from __future__ import annotations

from datetime import datetime, timezone


def ensure_utc(dt: datetime) -> datetime:
    """Normalize datetimes to timezone-aware UTC.

    Naive datetimes are interpreted as UTC (documented ingest rule).
    """
    if dt.tzinfo is None:
        return dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)


def serialize_utc_datetime(dt: datetime | None) -> str | None:
    if dt is None:
        return None
    return ensure_utc(dt).isoformat()
