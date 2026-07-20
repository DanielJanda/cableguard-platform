from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, Field, field_serializer

from app.core.datetime_utils import serialize_utc_datetime


class AcknowledgementCreate(BaseModel):
    acknowledged_by: str = Field(min_length=1, max_length=128)
    kiosk_id: str = Field(min_length=1, max_length=128)
    note: str | None = None


class AcknowledgementRead(BaseModel):
    acknowledgement_id: str
    event_id: str
    acknowledged_at: datetime
    acknowledged_by: str
    kiosk_id: str
    note: str | None

    model_config = {"from_attributes": True}

    @field_serializer("acknowledged_at")
    def serialize_acknowledged_at(self, value: datetime) -> str:
        return serialize_utc_datetime(value) or ""
