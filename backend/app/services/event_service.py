from __future__ import annotations

from datetime import datetime, timezone

from uuid import uuid4

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.db.models import Acknowledgement, Event
from app.schemas.acknowledgements import AcknowledgementCreate
from app.schemas.events import EventCreate
from app.services.websocket_manager import ws_manager


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


def create_event(db: Session, body: EventCreate) -> tuple[Event, bool]:
    """Insert event. Returns (event, created). Idempotent on event_id."""
    existing = db.scalar(select(Event).where(Event.event_id == body.event_id))
    if existing is not None:
        return existing, False

    row = Event(
        event_id=body.event_id,
        event_type=body.event_type,
        severity=body.severity,
        site_id=body.site_id,
        station_id=body.station_id,
        camera_id=body.camera_id,
        service_id=body.service_id,
        created_at=body.created_at,
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
    db.add(row)
    db.commit()
    db.refresh(row)
    return row, True


async def publish_event_created(event: Event) -> None:
    await ws_manager.broadcast(
        ws_manager.envelope(
            "event.created",
            {
                "event_id": event.event_id,
                "event_type": event.event_type,
                "severity": event.severity,
                "site_id": event.site_id,
                "station_id": event.station_id,
                "camera_id": event.camera_id,
                "service_id": event.service_id,
                "status": event.status,
                "risk_score": event.risk_score,
                "created_at": event.created_at.isoformat() if event.created_at else None,
            },
        )
    )


def list_events(
    db: Session,
    *,
    site_id: str | None = None,
    station_id: str | None = None,
    event_type: str | None = None,
    status: str | None = None,
    service_id: str | None = None,
    limit: int = 50,
    offset: int = 0,
) -> tuple[list[Event], int]:
    q = select(Event)
    count_q = select(func.count()).select_from(Event)
    if site_id:
        q = q.where(Event.site_id == site_id)
        count_q = count_q.where(Event.site_id == site_id)
    if station_id:
        q = q.where(Event.station_id == station_id)
        count_q = count_q.where(Event.station_id == station_id)
    if event_type:
        q = q.where(Event.event_type == event_type)
        count_q = count_q.where(Event.event_type == event_type)
    if status:
        q = q.where(Event.status == status)
        count_q = count_q.where(Event.status == status)
    if service_id:
        q = q.where(Event.service_id == service_id)
        count_q = count_q.where(Event.service_id == service_id)
    total = int(db.scalar(count_q) or 0)
    rows = list(
        db.scalars(q.order_by(Event.created_at.desc()).limit(limit).offset(offset)).all()
    )
    return rows, total


def get_event(db: Session, event_id: str) -> Event | None:
    return db.scalar(select(Event).where(Event.event_id == event_id))


def acknowledge_event(
    db: Session, event_id: str, body: AcknowledgementCreate
) -> tuple[Event, Acknowledgement]:
    event = get_event(db, event_id)
    if event is None:
        raise KeyError(event_id)
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
    return event, ack


async def publish_event_acknowledged(event: Event, ack: Acknowledgement) -> None:
    await ws_manager.broadcast(
        ws_manager.envelope(
            "event.acknowledged",
            {
                "event_id": event.event_id,
                "acknowledgement_id": ack.acknowledgement_id,
                "acknowledged_by": ack.acknowledged_by,
                "kiosk_id": ack.kiosk_id,
                "acknowledged_at": ack.acknowledged_at.isoformat(),
                "status": event.status,
            },
        )
    )
