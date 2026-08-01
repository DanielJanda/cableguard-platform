from __future__ import annotations

from datetime import datetime, timezone
from typing import Any
from uuid import uuid4

from sqlalchemy import JSON, Boolean, DateTime, Float, ForeignKey, String, Text, UniqueConstraint
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class Base(DeclarativeBase):
    pass


class Event(Base):
    __tablename__ = "events"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    event_id: Mapped[str] = mapped_column(String(64), unique=True, index=True, nullable=False)
    event_type: Mapped[str] = mapped_column(String(64), index=True, nullable=False)
    severity: Mapped[str] = mapped_column(String(32), default="warning", nullable=False)
    site_id: Mapped[str] = mapped_column(String(64), index=True, nullable=False)
    station_id: Mapped[str] = mapped_column(String(64), index=True, nullable=False)
    camera_id: Mapped[str | None] = mapped_column(String(64), nullable=True)
    service_id: Mapped[str] = mapped_column(String(128), index=True, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    received_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    risk_score: Mapped[float | None] = mapped_column(Float, nullable=True)
    status: Mapped[str] = mapped_column(String(32), default="open", index=True, nullable=False)
    snapshot_url: Mapped[str | None] = mapped_column(Text, nullable=True)
    clip_url: Mapped[str | None] = mapped_column(Text, nullable=True)
    snapshot_status: Mapped[str] = mapped_column(
        String(32), default="NOT_REQUESTED", nullable=False
    )
    clip_status: Mapped[str] = mapped_column(
        String(32), default="NOT_REQUESTED", nullable=False
    )
    algorithm_version: Mapped[str | None] = mapped_column(String(64), nullable=True)
    model_sha256: Mapped[str | None] = mapped_column(String(64), nullable=True)
    config_sha256: Mapped[str | None] = mapped_column(String(64), nullable=True)
    payload_json: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    updated_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)


class IncidentClipJob(Base):
    __tablename__ = "incident_clip_jobs"
    __table_args__ = (UniqueConstraint("event_id", name="uq_incident_clip_jobs_event_id"),)

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    event_id: Mapped[str] = mapped_column(
        String(64), ForeignKey("events.event_id"), index=True, nullable=False, unique=True
    )
    camera_id: Mapped[str] = mapped_column(String(64), nullable=False)
    mediamtx_path: Mapped[str] = mapped_column(String(128), nullable=False)
    requested_start: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    requested_end: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    available_after: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    status: Mapped[str] = mapped_column(String(32), default="PENDING", index=True, nullable=False)
    attempts: Mapped[int] = mapped_column(default=0, nullable=False)
    next_attempt_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    last_error: Mapped[str | None] = mapped_column(Text, nullable=True)
    temp_path: Mapped[str | None] = mapped_column(Text, nullable=True)
    final_path: Mapped[str | None] = mapped_column(Text, nullable=True)
    snapshot_path: Mapped[str | None] = mapped_column(Text, nullable=True)
    clip_sha256: Mapped[str | None] = mapped_column(String(64), nullable=True)
    snapshot_sha256: Mapped[str | None] = mapped_column(String(64), nullable=True)
    actual_duration_sec: Mapped[float | None] = mapped_column(Float, nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)


class ServiceHealth(Base):
    __tablename__ = "service_health"

    service_id: Mapped[str] = mapped_column(String(128), primary_key=True)
    site_id: Mapped[str] = mapped_column(String(64), index=True, nullable=False)
    station_id: Mapped[str | None] = mapped_column(String(64), nullable=True)
    service_type: Mapped[str] = mapped_column(String(64), nullable=False)
    status: Mapped[str] = mapped_column(String(32), default="offline", index=True, nullable=False)
    last_heartbeat_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    camera_connected: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    inference_running: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    relay_connected: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    last_error: Mapped[str | None] = mapped_column(Text, nullable=True)
    details_json: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)


class ServiceStatusHistory(Base):
    __tablename__ = "service_status_history"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    service_id: Mapped[str] = mapped_column(String(128), index=True, nullable=False)
    site_id: Mapped[str] = mapped_column(String(64), index=True, nullable=False)
    from_status: Mapped[str | None] = mapped_column(String(32), nullable=True)
    to_status: Mapped[str] = mapped_column(String(32), nullable=False)
    changed_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    reason: Mapped[str | None] = mapped_column(Text, nullable=True)


class Acknowledgement(Base):
    __tablename__ = "acknowledgements"
    __table_args__ = (
        UniqueConstraint("acknowledgement_id", name="uq_ack_id"),
        UniqueConstraint("event_id", name="uq_ack_event_id"),
    )

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    acknowledgement_id: Mapped[str] = mapped_column(String(64), nullable=False)
    event_id: Mapped[str] = mapped_column(
        String(64),
        ForeignKey("events.event_id"),
        index=True,
        nullable=False,
        unique=True,
    )
    acknowledged_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    acknowledged_by: Mapped[str] = mapped_column(String(128), nullable=False)
    kiosk_id: Mapped[str] = mapped_column(String(128), nullable=False)
    note: Mapped[str | None] = mapped_column(Text, nullable=True)
