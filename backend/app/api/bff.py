"""Production BFF – browser never sees the kiosk API key."""

from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel
from sqlalchemy.orm import Session

from app.core.config import get_settings
from app.core.exceptions import AcknowledgementConflictError
from app.db.session import get_db
from app.schemas.acknowledgements import AcknowledgementCreate, AcknowledgementRead
from app.schemas.events import EventRead
from app.services import event_service

router = APIRouter(prefix="/bff", tags=["bff"])


class AcknowledgeResponse(BaseModel):
    event: EventRead
    acknowledgement: AcknowledgementRead


@router.post(
    "/events/{event_id}/acknowledge",
    response_model=AcknowledgeResponse,
)
async def bff_acknowledge(
    event_id: str,
    body: AcknowledgementCreate,
    db: Session = Depends(get_db),
) -> AcknowledgeResponse:
    """Server-side acknowledge: injects kiosk key from settings, never from the browser."""
    settings = get_settings()
    if not settings.kiosk_api_key:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="CABLEGUARD_KIOSK_API_KEY is not configured on Event Core (BFF).",
        )

    try:
        event, ack, outcome = event_service.acknowledge_event(db, event_id, body)
    except KeyError:
        raise HTTPException(status_code=404, detail="Event not found") from None
    except AcknowledgementConflictError:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Event already acknowledged with different details",
        ) from None
    if outcome == "created":
        await event_service.publish_event_acknowledged(event, ack)
    return AcknowledgeResponse(
        event=EventRead.model_validate(event),
        acknowledgement=AcknowledgementRead.model_validate(ack),
    )
