from __future__ import annotations

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field

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


class StatusHistoryRead(BaseModel):
    id: int
    service_id: str
    site_id: str
    from_status: str | None
    to_status: str
    changed_at: datetime
    reason: str | None

    model_config = {"from_attributes": True}


class SystemStatusResponse(BaseModel):
    services: list[ServiceHealthRead]
    open_events: int
    heartbeat_timeout_sec: float


class HealthResponse(BaseModel):
    ok: bool = True
    service: str = "cableguard-platform"
    version: str = "0.1.0"
