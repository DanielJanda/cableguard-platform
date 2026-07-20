from __future__ import annotations

from fastapi import APIRouter, Depends, Query
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.core.config import get_settings
from app.db.models import Event
from app.db.session import get_db
from app.schemas.health import (
    HealthResponse,
    ServiceHealthRead,
    StatusHistoryRead,
    SystemStatusResponse,
)
from app.services import health_service

router = APIRouter()


@router.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse()


@router.get("/status", response_model=SystemStatusResponse)
def system_status(db: Session = Depends(get_db)) -> SystemStatusResponse:
    settings = get_settings()
    services = health_service.list_services(db)
    open_events = int(
        db.scalar(select(func.count()).select_from(Event).where(Event.status == "open")) or 0
    )
    return SystemStatusResponse(
        services=[ServiceHealthRead.model_validate(s) for s in services],
        open_events=open_events,
        heartbeat_timeout_sec=settings.heartbeat_timeout_sec,
    )


@router.get("/status/history", response_model=list[StatusHistoryRead])
def status_history(
    db: Session = Depends(get_db),
    service_id: str | None = None,
    limit: int = Query(default=100, ge=1, le=500),
    offset: int = Query(default=0, ge=0),
) -> list[StatusHistoryRead]:
    rows = health_service.list_status_history(
        db, service_id=service_id, limit=limit, offset=offset
    )
    return [StatusHistoryRead.model_validate(r) for r in rows]
