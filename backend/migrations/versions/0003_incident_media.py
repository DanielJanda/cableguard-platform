"""incident clip jobs + event media status columns

Revision ID: 0003_incident_media
Revises: 0002_ack_event_fk
Create Date: 2026-07-30
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0003_incident_media"
down_revision = "0002_ack_event_fk"
branch_labels = None
depends_on = None


def upgrade() -> None:
    with op.batch_alter_table("events", schema=None) as batch_op:
        batch_op.add_column(
            sa.Column(
                "snapshot_status",
                sa.String(length=32),
                nullable=False,
                server_default="NOT_REQUESTED",
            )
        )
        batch_op.add_column(
            sa.Column(
                "clip_status",
                sa.String(length=32),
                nullable=False,
                server_default="NOT_REQUESTED",
            )
        )
        batch_op.add_column(sa.Column("updated_at", sa.DateTime(timezone=True), nullable=True))

    op.create_table(
        "incident_clip_jobs",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("event_id", sa.String(length=64), nullable=False),
        sa.Column("camera_id", sa.String(length=64), nullable=False),
        sa.Column("mediamtx_path", sa.String(length=128), nullable=False),
        sa.Column("requested_start", sa.DateTime(timezone=True), nullable=False),
        sa.Column("requested_end", sa.DateTime(timezone=True), nullable=False),
        sa.Column("available_after", sa.DateTime(timezone=True), nullable=False),
        sa.Column("status", sa.String(length=32), nullable=False),
        sa.Column("attempts", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("next_attempt_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("last_error", sa.Text(), nullable=True),
        sa.Column("temp_path", sa.Text(), nullable=True),
        sa.Column("final_path", sa.Text(), nullable=True),
        sa.Column("snapshot_path", sa.Text(), nullable=True),
        sa.Column("clip_sha256", sa.String(length=64), nullable=True),
        sa.Column("snapshot_sha256", sa.String(length=64), nullable=True),
        sa.Column("actual_duration_sec", sa.Float(), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False),
        sa.ForeignKeyConstraint(["event_id"], ["events.event_id"]),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("event_id", name="uq_incident_clip_jobs_event_id"),
    )
    op.create_index("ix_incident_clip_jobs_event_id", "incident_clip_jobs", ["event_id"])
    op.create_index("ix_incident_clip_jobs_status", "incident_clip_jobs", ["status"])
    op.create_index(
        "ix_incident_clip_jobs_next_attempt_at",
        "incident_clip_jobs",
        ["next_attempt_at"],
    )


def downgrade() -> None:
    op.drop_index("ix_incident_clip_jobs_next_attempt_at", table_name="incident_clip_jobs")
    op.drop_index("ix_incident_clip_jobs_status", table_name="incident_clip_jobs")
    op.drop_index("ix_incident_clip_jobs_event_id", table_name="incident_clip_jobs")
    op.drop_table("incident_clip_jobs")
    with op.batch_alter_table("events", schema=None) as batch_op:
        batch_op.drop_column("updated_at")
        batch_op.drop_column("clip_status")
        batch_op.drop_column("snapshot_status")
