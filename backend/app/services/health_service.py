from __future__ import annotations

from datetime import datetime, timezone

from sqlalchemy import select
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session

from app.core.datetime_utils import ensure_utc, serialize_utc_datetime
from app.db.models import ServiceHealth, ServiceStatusHistory
from app.schemas.health import HeartbeatCreate
from app.services.websocket_manager import ws_manager


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


def upsert_heartbeat(db: Session, body: HeartbeatCreate) -> tuple[ServiceHealth, bool]:
    """Update service_health. Returns (row, status_changed)."""
    now = ensure_utc(body.sent_at) if body.sent_at else _utcnow()

    row = db.get(ServiceHealth, body.service_id)
    status_changed = False

    try:
        if row is None:
            row = ServiceHealth(
                service_id=body.service_id,
                site_id=body.site_id,
                station_id=body.station_id,
                service_type=body.service_type,
                status=body.status,
                last_heartbeat_at=now,
                camera_connected=body.camera_connected,
                inference_running=body.inference_running,
                relay_connected=body.relay_connected,
                last_error=body.last_error,
                details_json=body.details_json,
                updated_at=now,
            )
            db.add(row)
            status_changed = True
            db.add(
                ServiceStatusHistory(
                    service_id=body.service_id,
                    site_id=body.site_id,
                    from_status=None,
                    to_status=body.status,
                    changed_at=now,
                    reason="first_heartbeat",
                )
            )
        else:
            if row.status != body.status:
                status_changed = True
                db.add(
                    ServiceStatusHistory(
                        service_id=body.service_id,
                        site_id=body.site_id,
                        from_status=row.status,
                        to_status=body.status,
                        changed_at=now,
                        reason="heartbeat_status_change",
                    )
                )
            row.site_id = body.site_id
            row.station_id = body.station_id
            row.service_type = body.service_type
            row.status = body.status
            row.last_heartbeat_at = now
            row.camera_connected = body.camera_connected
            row.inference_running = body.inference_running
            row.relay_connected = body.relay_connected
            row.last_error = body.last_error
            row.details_json = body.details_json
            row.updated_at = now

        db.commit()
        db.refresh(row)
        return row, status_changed
    except SQLAlchemyError:
        db.rollback()
        raise


def mark_offline_if_stale(
    db: Session, *, timeout_sec: float
) -> list[ServiceHealth]:
    now = _utcnow()
    changed: list[ServiceHealth] = []
    rows = list(db.scalars(select(ServiceHealth)).all())
    for row in rows:
        if row.status == "offline":
            continue
        if row.last_heartbeat_at is None:
            continue
        last = ensure_utc(row.last_heartbeat_at)
        age = (now - last).total_seconds()
        if age > timeout_sec:
            previous = row.status
            row.status = "offline"
            row.updated_at = now
            db.add(
                ServiceStatusHistory(
                    service_id=row.service_id,
                    site_id=row.site_id,
                    from_status=previous,
                    to_status="offline",
                    changed_at=now,
                    reason="heartbeat_timeout",
                )
            )
            changed.append(row)
    if not changed:
        return changed
    try:
        db.commit()
        for row in changed:
            db.refresh(row)
        return changed
    except SQLAlchemyError:
        db.rollback()
        raise


def list_services(db: Session) -> list[ServiceHealth]:
    return list(db.scalars(select(ServiceHealth).order_by(ServiceHealth.service_id)).all())


def list_status_history(
    db: Session,
    *,
    service_id: str | None = None,
    limit: int = 100,
    offset: int = 0,
) -> list[ServiceStatusHistory]:
    q = select(ServiceStatusHistory)
    if service_id:
        q = q.where(ServiceStatusHistory.service_id == service_id)
    q = q.order_by(ServiceStatusHistory.changed_at.desc()).limit(limit).offset(offset)
    return list(db.scalars(q).all())


async def publish_service_update(row: ServiceHealth, *, offline: bool = False) -> None:
    msg_type = "service.offline" if offline or row.status == "offline" else "service.updated"
    await ws_manager.broadcast(
        ws_manager.envelope(
            msg_type,
            {
                "service_id": row.service_id,
                "site_id": row.site_id,
                "station_id": row.station_id,
                "service_type": row.service_type,
                "status": row.status,
                "last_heartbeat_at": serialize_utc_datetime(row.last_heartbeat_at),
            },
        )
    )
