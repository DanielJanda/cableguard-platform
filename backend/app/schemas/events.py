from __future__ import annotations

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field


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


class EventListResponse(BaseModel):
    items: list[EventRead]
    total: int
    limit: int
    offset: int
