from __future__ import annotations

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel

WsType = Literal[
    "event.created",
    "event.acknowledged",
    "service.updated",
    "service.offline",
    "system.snapshot",
]


class WsEnvelope(BaseModel):
    type: WsType
    data: dict[str, Any]
    sent_at: datetime
