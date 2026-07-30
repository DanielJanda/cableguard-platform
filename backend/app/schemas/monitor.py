"""Safe monitor layout contracts — no credentials or RTSP secrets."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field


LocationRole = Literal["upper_station", "lower_station", "restraint_check", "other"]
EnvironmentKind = Literal["production", "test"]
LayoutType = Literal["1", "2", "3", "4"]
PipelineUiState = Literal["READY", "DEGRADED", "STOPPED", "NOT_CONFIGURED", "NOT_IMPLEMENTED"]
StreamUiState = Literal["LIVE", "STALE", "OFFLINE", "CONNECTING", "UNKNOWN"]
RecordingUiState = Literal["RECORDING", "DEGRADED", "OFF", "NOT_CONFIGURED"]


class PipelineSummary(BaseModel):
    pipeline_id: str
    detector_type: str
    display_name: str
    state: PipelineUiState = "NOT_CONFIGURED"
    event_type: str | None = None
    is_test: bool = False


class CameraTileConfig(BaseModel):
    camera_id: str
    display_name: str
    location_role: LocationRole
    environment: EnvironmentKind
    mediamtx_path: str
    whep_path: str
    monitor_visible: bool = True
    primary: bool = False
    synthetic: bool = False
    recording_state: RecordingUiState = "NOT_CONFIGURED"
    stream_hint: StreamUiState = "UNKNOWN"
    pipelines: list[PipelineSummary] = Field(default_factory=list)


class MonitorLayoutResponse(BaseModel):
    schema_version: int = 1
    layout_id: str
    layout_type: LayoutType
    site_id: str
    site_display_name: str
    lift_id: str
    lift_display_name: str
    primary_camera_id: str
    alarm_focus_policy: str = "highlight_keep_others"
    cameras: list[CameraTileConfig]
