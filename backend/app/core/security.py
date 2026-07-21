"""API key dependencies. Never log key values."""

from __future__ import annotations

import secrets

from fastapi import Header, HTTPException, status

from app.core.config import get_settings
from app.core.secrets import is_configured_secret


def _keys_match(provided: str | None, expected: str) -> bool:
    if not provided or not expected:
        return False
    return secrets.compare_digest(provided, expected)


async def require_ingest_api_key(
    x_api_key: str | None = Header(default=None, alias="X-API-Key"),
) -> None:
    expected = get_settings().ingest_api_key
    if not is_configured_secret(expected):
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail=(
                "Ingest API key is not configured on Event Core "
                "(set CABLEGUARD_INGEST_API_KEY in .env)."
            ),
        )
    if not _keys_match(x_api_key, expected):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid or missing ingest API key",
        )


async def require_kiosk_api_key(
    x_kiosk_key: str | None = Header(default=None, alias="X-Kiosk-Key"),
) -> None:
    expected = get_settings().kiosk_api_key
    if not is_configured_secret(expected):
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail=(
                "Kiosk API key is not configured on Event Core "
                "(set CABLEGUARD_KIOSK_API_KEY in .env)."
            ),
        )
    if not _keys_match(x_kiosk_key, expected):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid or missing kiosk API key",
        )
