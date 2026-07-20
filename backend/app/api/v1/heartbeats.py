from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.security import require_ingest_api_key
from app.db.session import get_db
from app.schemas.health import HeartbeatCreate, ServiceHealthRead
from app.services import health_service

router = APIRouter()


@router.post(
    "/heartbeats",
    response_model=ServiceHealthRead,
    dependencies=[Depends(require_ingest_api_key)],
)
async def post_heartbeat(
    body: HeartbeatCreate,
    db: Session = Depends(get_db),
) -> ServiceHealthRead:
    row, changed = health_service.upsert_heartbeat(db, body)
    # Always push update so UI sees fresh last_heartbeat_at; offline flag only when offline
    await health_service.publish_service_update(row, offline=(row.status == "offline"))
    return ServiceHealthRead.model_validate(row)
