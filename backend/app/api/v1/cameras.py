from __future__ import annotations

from fastapi import APIRouter, HTTPException, Query

from app.cameras.registry import get_camera, get_registry, list_enabled_cameras
from app.core.config import get_settings
from app.schemas.cameras import CameraListResponse, CameraRead, StreamStatusRead
from app.services import mediamtx_status

router = APIRouter()


@router.get("/cameras", response_model=CameraListResponse)
def list_cameras(enabled_only: bool = Query(default=False)) -> CameraListResponse:
    cameras = list_enabled_cameras() if enabled_only else get_registry().cameras
    items = [CameraRead.model_validate(c.model_dump()) for c in cameras]
    return CameraListResponse(items=items, total=len(items))


@router.get("/cameras/{camera_id}", response_model=CameraRead)
def get_camera_by_id(camera_id: str) -> CameraRead:
    camera = get_camera(camera_id)
    if camera is None:
        raise HTTPException(status_code=404, detail="Camera not found")
    return CameraRead.model_validate(camera.model_dump())


@router.get("/streams/{stream_name}/status", response_model=StreamStatusRead)
async def get_stream_status(stream_name: str) -> StreamStatusRead:
    settings = get_settings()
    raw, api_online = await mediamtx_status.fetch_path_status(
        stream_name,
        api_base=settings.mediamtx_api_url,
    )
    mapped = mediamtx_status.map_path_status(raw, api_online=api_online)
    return StreamStatusRead(stream_name=stream_name, **mapped)
