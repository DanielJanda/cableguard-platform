from __future__ import annotations

from fastapi import APIRouter

from app.schemas.monitor import MonitorLayoutResponse
from app.services import monitor_layout_service

router = APIRouter()


@router.get("/monitor/layouts", response_model=list[str])
def list_monitor_layouts() -> list[str]:
    """Public list of known layout ids (safe, no secrets)."""
    return monitor_layout_service.list_layout_ids()


@router.get("/monitor/layouts/{layout_id}", response_model=MonitorLayoutResponse)
def get_monitor_layout(layout_id: str) -> MonitorLayoutResponse:
    """Public read-only operator layout for Monitor UI."""
    return monitor_layout_service.get_layout(layout_id)
