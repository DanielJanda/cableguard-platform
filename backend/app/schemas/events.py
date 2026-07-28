from __future__ import annotations

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field, computed_field, field_serializer, field_validator

from app.core.datetime_utils import ensure_utc, serialize_utc_datetime
from app.core.media_urls import validate_media_url


EventStatus = Literal["open", "acknowledged", "closed"]
Severity = Literal["info", "warning", "alarm", "critical"]


class EventCreate(BaseModel):
    event_id: str = Field(min_length=1, max_length=64)
    event_type: str
    severity: Severity = "warning"
    site_id: str
    station_id: str
    camera_id: str | None = None
    service_id: str
    created_at: datetime
    risk_score: float | None = None
    snapshot_url: str | None = None
    clip_url: str | None = None
    algorithm_version: str | None = None
    model_sha256: str | None = None
    config_sha256: str | None = None
    payload_json: dict[str, Any] | None = None

    @field_validator("created_at")
    @classmethod
    def normalize_created_at(cls, value: datetime) -> datetime:
        return ensure_utc(value)

    @field_validator("snapshot_url", "clip_url")
    @classmethod
    def validate_media_fields(cls, value: str | None) -> str | None:
        return validate_media_url(value)


class EventRead(BaseModel):
    event_id: str
    event_type: str
    severity: str
    site_id: str
    station_id: str
    camera_id: str | None
    service_id: str
    created_at: datetime
    received_at: datetime
    risk_score: float | None
    status: str
    snapshot_url: str | None
    clip_url: str | None
    algorithm_version: str | None
    model_sha256: str | None
    config_sha256: str | None
    payload_json: dict[str, Any] | None

    model_config = {"from_attributes": True}

    @computed_field  # type: ignore[prop-decorator]
    @property
    def is_test(self) -> bool:
        """True when payload_json.test_mode is explicitly true (office / lab events)."""
        return is_test_payload(self.payload_json)

    @field_serializer("created_at", "received_at")
    def serialize_datetimes(self, value: datetime) -> str:
        return serialize_utc_datetime(value) or ""


def is_test_payload(payload: dict[str, Any] | None) -> bool:
    return isinstance(payload, dict) and payload.get("test_mode") is True


class EventListResponse(BaseModel):
    items: list[EventRead]
    total: int
    limit: int
    offset: int
