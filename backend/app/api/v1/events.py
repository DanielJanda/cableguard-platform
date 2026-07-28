from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, Query, Response, status
from sqlalchemy.orm import Session

from app.core.security import require_ingest_api_key
from app.db.session import get_db
from app.schemas.events import EventCreate, EventListResponse, EventRead
from app.services import event_service

router = APIRouter()


@router.post(
    "/events",
    response_model=EventRead,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_ingest_api_key)],
)
async def post_event(
    body: EventCreate,
    response: Response,
    db: Session = Depends(get_db),
) -> EventRead:
    row, outcome = event_service.create_event(db, body)
    if outcome == "conflict":
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Event ID already exists with a different payload",
        )
    if outcome == "created":
        await event_service.publish_event_created(row)
    else:
        response.status_code = status.HTTP_200_OK
    return EventRead.model_validate(row)


@router.get("/events", response_model=EventListResponse)
def get_events(
    db: Session = Depends(get_db),
    site_id: str | None = None,
    station_id: str | None = None,
    event_type: str | None = None,
    status_filter: str | None = Query(default=None, alias="status"),
    service_id: str | None = None,
    include_test_events: bool = Query(
        default=False,
        description="When false (default), exclude events with payload_json.test_mode=true.",
    ),
    limit: int = Query(default=50, ge=1, le=200),
    offset: int = Query(default=0, ge=0),
) -> EventListResponse:
    rows, total = event_service.list_events(
        db,
        site_id=site_id,
        station_id=station_id,
        event_type=event_type,
        status=status_filter,
        service_id=service_id,
        include_test_events=include_test_events,
        limit=limit,
        offset=offset,
    )
    return EventListResponse(
        items=[EventRead.model_validate(r) for r in rows],
        total=total,
        limit=limit,
        offset=offset,
    )


@router.get("/events/{event_id}", response_model=EventRead)
def get_event(event_id: str, db: Session = Depends(get_db)) -> EventRead:
    row = event_service.get_event(db, event_id)
    if row is None:
        raise HTTPException(status_code=404, detail="Event not found")
    return EventRead.model_validate(row)
