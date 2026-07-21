from __future__ import annotations

from pydantic import BaseModel


class CameraRead(BaseModel):
    camera_id: str
    site_id: str
    station_id: str
    stream_name: str
    rtsp_proxy_path: str
    whep_path: str
    enabled: bool


class CameraListResponse(BaseModel):
    items: list[CameraRead]
    total: int


class StreamStatusRead(BaseModel):
    stream_name: str
    mediamtx_ready: bool | None
    mediamtx_online: bool
    readers: int | None
    source_ready: bool | None
    status: str
    detail: str | None = None
