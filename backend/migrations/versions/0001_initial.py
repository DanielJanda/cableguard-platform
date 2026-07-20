"""initial schema: events, service_health, service_status_history, acknowledgements

Revision ID: 0001_initial
Revises:
Create Date: 2026-07-20
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0001_initial"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "events",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("event_id", sa.String(length=64), nullable=False),
        sa.Column("event_type", sa.String(length=64), nullable=False),
        sa.Column("severity", sa.String(length=32), nullable=False),
        sa.Column("site_id", sa.String(length=64), nullable=False),
        sa.Column("station_id", sa.String(length=64), nullable=False),
        sa.Column("camera_id", sa.String(length=64), nullable=True),
        sa.Column("service_id", sa.String(length=128), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("received_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("risk_score", sa.Float(), nullable=True),
        sa.Column("status", sa.String(length=32), nullable=False),
        sa.Column("snapshot_url", sa.Text(), nullable=True),
        sa.Column("clip_url", sa.Text(), nullable=True),
        sa.Column("algorithm_version", sa.String(length=64), nullable=True),
        sa.Column("model_sha256", sa.String(length=64), nullable=True),
        sa.Column("config_sha256", sa.String(length=64), nullable=True),
        sa.Column("payload_json", sa.JSON(), nullable=True),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("event_id"),
    )
    op.create_index("ix_events_event_id", "events", ["event_id"])
    op.create_index("ix_events_event_type", "events", ["event_type"])
    op.create_index("ix_events_site_id", "events", ["site_id"])
    op.create_index("ix_events_station_id", "events", ["station_id"])
    op.create_index("ix_events_service_id", "events", ["service_id"])
    op.create_index("ix_events_status", "events", ["status"])

    op.create_table(
        "service_health",
        sa.Column("service_id", sa.String(length=128), nullable=False),
        sa.Column("site_id", sa.String(length=64), nullable=False),
        sa.Column("station_id", sa.String(length=64), nullable=True),
        sa.Column("service_type", sa.String(length=64), nullable=False),
        sa.Column("status", sa.String(length=32), nullable=False),
        sa.Column("last_heartbeat_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("camera_connected", sa.Boolean(), nullable=True),
        sa.Column("inference_running", sa.Boolean(), nullable=True),
        sa.Column("relay_connected", sa.Boolean(), nullable=True),
        sa.Column("last_error", sa.Text(), nullable=True),
        sa.Column("details_json", sa.JSON(), nullable=True),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
        sa.PrimaryKeyConstraint("service_id"),
    )
    op.create_index("ix_service_health_site_id", "service_health", ["site_id"])
    op.create_index("ix_service_health_status", "service_health", ["status"])

    op.create_table(
        "service_status_history",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("service_id", sa.String(length=128), nullable=False),
        sa.Column("site_id", sa.String(length=64), nullable=False),
        sa.Column("from_status", sa.String(length=32), nullable=True),
        sa.Column("to_status", sa.String(length=32), nullable=False),
        sa.Column("changed_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("reason", sa.Text(), nullable=True),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(
        "ix_service_status_history_service_id", "service_status_history", ["service_id"]
    )
    op.create_index("ix_service_status_history_site_id", "service_status_history", ["site_id"])

    op.create_table(
        "acknowledgements",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("acknowledgement_id", sa.String(length=64), nullable=False),
        sa.Column("event_id", sa.String(length=64), nullable=False),
        sa.Column("acknowledged_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("acknowledged_by", sa.String(length=128), nullable=False),
        sa.Column("kiosk_id", sa.String(length=128), nullable=False),
        sa.Column("note", sa.Text(), nullable=True),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("acknowledgement_id", name="uq_ack_id"),
    )
    op.create_index("ix_acknowledgements_event_id", "acknowledgements", ["event_id"])


def downgrade() -> None:
    op.drop_table("acknowledgements")
    op.drop_table("service_status_history")
    op.drop_table("service_health")
    op.drop_table("events")
