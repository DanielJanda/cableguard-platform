from __future__ import annotations

from fastapi import APIRouter, WebSocket, WebSocketDisconnect
from sqlalchemy import func, select

from app.core.config import get_settings
from app.db.models import Event
from app.db import session as db_session
from app.schemas.health import ServiceHealthRead
from app.services import health_service
from app.services.websocket_manager import ws_manager

router = APIRouter()


@router.websocket("/ws/v1")
async def websocket_endpoint(websocket: WebSocket) -> None:
    await ws_manager.connect(websocket)
    try:
        # system.snapshot on connect
        factory = db_session.SessionLocal
        if factory is not None:
            db = factory()
            try:
                services = health_service.list_services(db)
                open_events = int(
                    db.scalar(select(func.count()).select_from(Event).where(Event.status == "open"))
                    or 0
                )
                settings = get_settings()
                snap = ws_manager.envelope(
                    "system.snapshot",
                    {
                        "services": [
                            ServiceHealthRead.model_validate(s).model_dump(mode="json")
                            for s in services
                        ],
                        "open_events": open_events,
                        "heartbeat_timeout_sec": settings.heartbeat_timeout_sec,
                    },
                )
                await websocket.send_json(snap)
            finally:
                db.close()

        while True:
            # Keep connection; client may send ping frames as text
            await websocket.receive_text()
    except WebSocketDisconnect:
        await ws_manager.disconnect(websocket)
    except Exception:
        await ws_manager.disconnect(websocket)
