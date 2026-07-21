"""Read-only MediaMTX Control API helpers – never logs credentials."""

from __future__ import annotations

import logging
from typing import Any

import httpx

logger = logging.getLogger(__name__)


def map_path_status(raw: dict[str, Any] | None, *, api_online: bool) -> dict[str, Any]:
    if not api_online:
        return {
            "mediamtx_online": False,
            "mediamtx_ready": None,
            "readers": None,
            "source_ready": None,
            "status": "mediamtx_offline",
            "detail": "MediaMTX Control API unreachable",
        }
    if raw is None:
        return {
            "mediamtx_online": True,
            "mediamtx_ready": False,
            "readers": 0,
            "source_ready": False,
            "status": "path_not_found",
            "detail": "Path not configured in MediaMTX",
        }
    ready = bool(raw.get("ready"))
    readers = len(raw.get("readers") or [])
    source = raw.get("source") or {}
    source_ready = bool(source.get("ready")) if isinstance(source, dict) else ready
    if not ready:
        status = "source_not_ready"
        detail = "MediaMTX path exists but source is not ready"
    elif readers == 0:
        status = "ready"
        detail = "Path ready, no active readers"
    else:
        status = "streaming"
        detail = None
    return {
        "mediamtx_online": True,
        "mediamtx_ready": ready,
        "readers": readers,
        "source_ready": source_ready,
        "status": status,
        "detail": detail,
    }


async def fetch_path_status(
    stream_name: str,
    *,
    api_base: str = "http://127.0.0.1:9997",
    timeout_sec: float = 2.0,
) -> tuple[dict[str, Any] | None, bool]:
    url = f"{api_base.rstrip('/')}/v3/paths/get/{stream_name}"
    try:
        async with httpx.AsyncClient(timeout=timeout_sec) as client:
            response = await client.get(url)
            if response.status_code == 404:
                return None, True
            response.raise_for_status()
            payload = response.json()
            if isinstance(payload, dict):
                return payload, True
            return None, True
    except httpx.HTTPError as exc:
        logger.warning("MediaMTX status query failed: %s", type(exc).__name__)
        return None, False
