from __future__ import annotations

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field, field_serializer, field_validator

from app.core.datetime_utils import ensure_utc, serialize_utc_datetime

ServiceStatus = Literal["healthy", "degraded", "offline"]


class HeartbeatCreate(BaseModel):
    service_id: str
    site_id: str
    station_id: str | None = None
    service_type: str
    status: ServiceStatus = "healthy"
    camera_connected: bool | None = None
    inference_running: bool | None = None
    relay_connected: bool | None = None
    last_error: str | None = None
    details_json: dict[str, Any] | None = None
    sent_at: datetime | None = None

    @field_validator("sent_at")
    @classmethod
    def normalize_sent_at(cls, value: datetime | None) -> datetime | None:
        if value is None:
            return None
        return ensure_utc(value)


class ServiceHealthRead(BaseModel):
    service_id: str
    site_id: str
    station_id: str | None
    service_type: str
    status: str
    last_heartbeat_at: datetime | None
    camera_connected: bool | None
    inference_running: bool | None
    relay_connected: bool | None
    last_error: str | None
    details_json: dict[str, Any] | None
    updated_at: datetime

    model_config = {"from_attributes": True}

    @field_serializer("last_heartbeat_at", "updated_at")
    def serialize_datetimes(self, value: datetime | None) -> str | None:
        if value is None:
            return None
        return serialize_utc_datetime(value)


class StatusHistoryRead(BaseModel):
    id: int
    service_id: str
    site_id: str
    from_status: str | None
    to_status: str
    changed_at: datetime
    reason: str | None

    model_config = {"from_attributes": True}

    @field_serializer("changed_at")
    def serialize_changed_at(self, value: datetime) -> str:
        return serialize_utc_datetime(value) or ""


class SystemStatusResponse(BaseModel):
    services: list[ServiceHealthRead]
    open_events: int
    heartbeat_timeout_sec: float


class HealthResponse(BaseModel):
    ok: bool = True
    service: str = "cableguard-platform"
    version: str = "0.1.0"
