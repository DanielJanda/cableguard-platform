from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session

from app.db.session import get_db
from app.schemas.acknowledgements import AcknowledgementCreate, AcknowledgementRead
from app.schemas.events import EventRead
from app.services import event_service
from pydantic import BaseModel

router = APIRouter()


class AcknowledgeResponse(BaseModel):
    event: EventRead
    acknowledgement: AcknowledgementRead


@router.post("/events/{event_id}/acknowledge", response_model=AcknowledgeResponse)
async def acknowledge(
    event_id: str,
    body: AcknowledgementCreate,
    db: Session = Depends(get_db),
) -> AcknowledgeResponse:
    try:
        event, ack = event_service.acknowledge_event(db, event_id, body)
    except KeyError:
        raise HTTPException(status_code=404, detail="Event not found") from None
    await event_service.publish_event_acknowledged(event, ack)
    return AcknowledgeResponse(
        event=EventRead.model_validate(event),
        acknowledgement=AcknowledgementRead.model_validate(ack),
    )
