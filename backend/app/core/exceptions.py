from __future__ import annotations


class EventPayloadConflictError(Exception):
    """Raised when event_id exists with a different semantic payload."""


class AcknowledgementConflictError(Exception):
    """Raised when an event is already acknowledged with different details."""
