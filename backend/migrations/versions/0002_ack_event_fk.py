"""add acknowledgement event_id uniqueness and foreign key

Revision ID: 0002_ack_event_fk
Revises: 0001_initial
Create Date: 2026-07-20
"""

from __future__ import annotations

from alembic import op

revision = "0002_ack_event_fk"
down_revision = "0001_initial"
branch_labels = None
depends_on = None


def upgrade() -> None:
    with op.batch_alter_table("acknowledgements", schema=None) as batch_op:
        batch_op.create_unique_constraint("uq_ack_event_id", ["event_id"])
        batch_op.create_foreign_key(
            "fk_ack_event_id",
            "events",
            ["event_id"],
            ["event_id"],
        )


def downgrade() -> None:
    with op.batch_alter_table("acknowledgements", schema=None) as batch_op:
        batch_op.drop_constraint("fk_ack_event_id", type_="foreignkey")
        batch_op.drop_constraint("uq_ack_event_id", type_="unique")
