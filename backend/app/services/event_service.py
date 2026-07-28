from __future__ import annotations

from datetime import datetime, timezone
from typing import Literal
from uuid import uuid4

from sqlalchemy import select
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session

from app.core.datetime_utils import ensure_utc
from app.core.exceptions import AcknowledgementConflictError, EventPayloadConflictError
from app.db.models import Acknowledgement, Event
from app.schemas.acknowledgements import AcknowledgementCreate
from app.schemas.events import EventCreate, EventRead
from app.services.event_payload import events_payload_match
from app.services.websocket_manager import ws_manager

CreateEventOutcome = Literal["created", "duplicate", "conflict"]
AcknowledgeOutcome = Literal["created", "duplicate", "conflict"]


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


def _ack_body_match(ack: Acknowledgement, body: AcknowledgementCreate) -> bool:
    existing_note = (ack.note or "").strip() or None
    incoming_note = (body.note or "").strip() or None
    return (
        ack.acknowledged_by == body.acknowledged_by
        and ack.kiosk_id == body.kiosk_id
        and existing_note == incoming_note
    )


def create_event(db: Session, body: EventCreate) -> tuple[Event, CreateEventOutcome]:
    """Insert event or return duplicate/conflict outcome for idempotent ingest."""
    existing = db.scalar(select(Event).where(Event.event_id == body.event_id))
    if existing is not None:
        if events_payload_match(existing, body):
            return existing, "duplicate"
        return existing, "conflict"

    row = Event(
        event_id=body.event_id,
        event_type=body.event_type,
        severity=body.severity,
        site_id=body.site_id,
        station_id=body.station_id,
        camera_id=body.camera_id,
        service_id=body.service_id,
        created_at=ensure_utc(body.created_at),
        received_at=_utcnow(),
        risk_score=body.risk_score,
        status="open",
        snapshot_url=body.snapshot_url,
        clip_url=body.clip_url,
        algorithm_version=body.algorithm_version,
        model_sha256=body.model_sha256,
        config_sha256=body.config_sha256,
        payload_json=body.payload_json,
    )
    try:
        db.add(row)
        db.commit()
        db.refresh(row)
        return row, "created"
    except SQLAlchemyError:
        db.rollback()
        raise


def event_created_ws_data(event: Event) -> dict:
    return EventRead.model_validate(event).model_dump(
        mode="json",
        exclude={"payload_json"},
    )


async def publish_event_created(event: Event) -> None:
    await ws_manager.broadcast(
        ws_manager.envelope("event.created", event_created_ws_data(event))
    )


def list_events(
    db: Session,
    *,
    site_id: str | None = None,
    station_id: str | None = None,
    event_type: str | None = None,
    status: str | None = None,
    service_id: str | None = None,
    include_test_events: bool = False,
    limit: int = 50,
    offset: int = 0,
) -> tuple[list[Event], int]:
    from app.schemas.events import is_test_payload

    q = select(Event)
    if site_id:
        q = q.where(Event.site_id == site_id)
    if station_id:
        q = q.where(Event.station_id == station_id)
    if event_type:
        q = q.where(Event.event_type == event_type)
    if status:
        q = q.where(Event.status == status)
    if service_id:
        q = q.where(Event.service_id == service_id)

    # Filter test events in-process so SQLite/Postgres JSON shapes stay portable.
    ordered = list(db.scalars(q.order_by(Event.created_at.desc())).all())
    if not include_test_events:
        ordered = [r for r in ordered if not is_test_payload(r.payload_json)]
    total = len(ordered)
    rows = ordered[offset : offset + limit]
    return rows, total


def get_event(db: Session, event_id: str) -> Event | None:
    return db.scalar(select(Event).where(Event.event_id == event_id))


def get_acknowledgement_for_event(db: Session, event_id: str) -> Acknowledgement | None:
    return db.scalar(select(Acknowledgement).where(Acknowledgement.event_id == event_id))


def acknowledge_event(
    db: Session, event_id: str, body: AcknowledgementCreate
) -> tuple[Event, Acknowledgement, AcknowledgeOutcome]:
    event = get_event(db, event_id)
    if event is None:
        raise KeyError(event_id)

    existing_ack = get_acknowledgement_for_event(db, event_id)
    if existing_ack is not None:
        if _ack_body_match(existing_ack, body):
            return event, existing_ack, "duplicate"
        raise AcknowledgementConflictError(event_id)

    try:
        event.status = "acknowledged"
        ack = Acknowledgement(
            acknowledgement_id=str(uuid4()),
            event_id=event_id,
            acknowledged_at=_utcnow(),
            acknowledged_by=body.acknowledged_by,
            kiosk_id=body.kiosk_id,
            note=body.note,
        )
        db.add(ack)
        db.commit()
        db.refresh(event)
        db.refresh(ack)
        return event, ack, "created"
    except SQLAlchemyError:
        db.rollback()
        raise


async def publish_event_acknowledged(event: Event, ack: Acknowledgement) -> None:
    from app.schemas.acknowledgements import AcknowledgementRead

    ack_data = AcknowledgementRead.model_validate(ack).model_dump(mode="json")
    await ws_manager.broadcast(
        ws_manager.envelope(
            "event.acknowledged",
            {
                "event_id": event.event_id,
                "acknowledgement_id": ack.acknowledgement_id,
                "acknowledged_by": ack.acknowledged_by,
                "kiosk_id": ack.kiosk_id,
                "acknowledged_at": ack_data["acknowledged_at"],
                "status": event.status,
            },
        )
    )
