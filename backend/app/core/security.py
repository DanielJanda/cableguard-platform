"""Ingest API key dependency. Never log the key value."""

from __future__ import annotations

from fastapi import Header, HTTPException, status

from app.core.config import get_settings


async def require_ingest_api_key(
    x_api_key: str | None = Header(default=None, alias="X-API-Key"),
) -> None:
    expected = get_settings().ingest_api_key
    if not expected or expected == "change-me-local-dev-key":
        # Still require header match to configured value (including placeholder in dev)
        pass
    if not x_api_key or x_api_key != expected:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid or missing ingest API key",
        )
