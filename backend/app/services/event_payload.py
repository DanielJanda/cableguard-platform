from __future__ import annotations

import json
from typing import Any

from app.core.datetime_utils import ensure_utc
from app.db.models import Event
from app.schemas.events import EventCreate


def _normalize_optional_str(value: str | None) -> str | None:
    if value is None:
        return None
    stripped = value.strip()
    return stripped or None


def _normalize_payload(value: dict[str, Any] | None) -> str | None:
    if value is None:
        return None
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def canonical_event_payload(body: EventCreate, *, status: str = "open") -> dict[str, Any]:
    return {
        "event_type": body.event_type,
        "severity": body.severity,
        "site_id": body.site_id,
        "station_id": body.station_id,
        "camera_id": _normalize_optional_str(body.camera_id),
        "service_id": body.service_id,
        "created_at": ensure_utc(body.created_at).isoformat(),
        "risk_score": body.risk_score,
        "status": status,
        "snapshot_url": _normalize_optional_str(body.snapshot_url),
        "clip_url": _normalize_optional_str(body.clip_url),
        "algorithm_version": _normalize_optional_str(body.algorithm_version),
        "model_sha256": _normalize_optional_str(body.model_sha256),
        "config_sha256": _normalize_optional_str(body.config_sha256),
        "payload_json": _normalize_payload(body.payload_json),
    }


def canonical_stored_event(event: Event) -> dict[str, Any]:
    return {
        "event_type": event.event_type,
        "severity": event.severity,
        "site_id": event.site_id,
        "station_id": event.station_id,
        "camera_id": _normalize_optional_str(event.camera_id),
        "service_id": event.service_id,
        "created_at": ensure_utc(event.created_at).isoformat(),
        "risk_score": event.risk_score,
        "status": event.status,
        "snapshot_url": _normalize_optional_str(event.snapshot_url),
        "clip_url": _normalize_optional_str(event.clip_url),
        "algorithm_version": _normalize_optional_str(event.algorithm_version),
        "model_sha256": _normalize_optional_str(event.model_sha256),
        "config_sha256": _normalize_optional_str(event.config_sha256),
        "payload_json": _normalize_payload(event.payload_json),
    }


def events_payload_match(existing: Event, body: EventCreate) -> bool:
    return canonical_stored_event(existing) == canonical_event_payload(body, status="open")
