"""Serve production CableGuard monitor UI from Event Core.

Preferred path: reverse-proxy SSR HTML/assets from the local Nitro node-server
(so deep routes refresh correctly). Fallback: StaticFiles + SPA index.html when
``CABLEGUARD_MONITOR_STATIC_DIR`` points at a prepared SPA tree.
"""

from __future__ import annotations

import logging
from pathlib import Path

import httpx
from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles

from app.core.config import Settings

logger = logging.getLogger(__name__)

_RESERVED_PREFIXES = (
    "api/",
    "bff/",
    "ws/",
    "docs",
    "openapi.json",
    "redoc",
)


def _is_reserved(path: str) -> bool:
    p = path.lstrip("/")
    return any(p == pref.rstrip("/") or p.startswith(pref) for pref in _RESERVED_PREFIXES)


def mount_monitor_ui(app: FastAPI, settings: Settings) -> None:
    upstream = (settings.monitor_ui_upstream or "").rstrip("/")
    static_dir = (settings.monitor_static_dir or "").strip()
    static_path = Path(static_dir) if static_dir else None

    if upstream:
        _mount_upstream_proxy(app, upstream)
        logger.info("Monitor UI reverse-proxy → %s", upstream)
        return

    if static_path and static_path.is_dir():
        _mount_static_spa(app, static_path)
        logger.info("Monitor UI StaticFiles ← %s", static_path)
        return

    logger.info(
        "Monitor UI not mounted (set CABLEGUARD_MONITOR_UI_UPSTREAM or CABLEGUARD_MONITOR_STATIC_DIR)"
    )


def _mount_upstream_proxy(app: FastAPI, upstream: str) -> None:
    client = httpx.AsyncClient(base_url=upstream, timeout=60.0, follow_redirects=False)
    app.state.monitor_ui_client = client

    async def proxy(request: Request, full_path: str = "") -> Response:
        if _is_reserved(full_path):
            raise HTTPException(status_code=404, detail="Not found")

        url = f"/{full_path}" if full_path else "/"
        if request.url.query:
            url = f"{url}?{request.url.query}"

        headers = {
            k: v
            for k, v in request.headers.items()
            if k.lower() not in {"host", "content-length", "connection"}
        }
        body = await request.body()
        try:
            upstream_resp = await client.request(
                request.method,
                url,
                headers=headers,
                content=body if body else None,
            )
        except httpx.RequestError as exc:
            raise HTTPException(
                status_code=502,
                detail=f"Monitor UI upstream unreachable ({upstream}): {exc}",
            ) from exc

        excluded = {"content-encoding", "transfer-encoding", "connection"}
        out_headers = {
            k: v for k, v in upstream_resp.headers.items() if k.lower() not in excluded
        }
        return Response(
            content=upstream_resp.content,
            status_code=upstream_resp.status_code,
            headers=out_headers,
            media_type=upstream_resp.headers.get("content-type"),
        )

    app.add_api_route(
        "/",
        proxy,
        methods=["GET", "HEAD"],
        include_in_schema=False,
        name="monitor_ui_root",
    )
    app.add_api_route(
        "/{full_path:path}",
        proxy,
        methods=["GET", "HEAD"],
        include_in_schema=False,
        name="monitor_ui_proxy",
    )


def _mount_static_spa(app: FastAPI, static_path: Path) -> None:
    index = static_path / "index.html"
    assets = static_path / "assets"
    if assets.is_dir():
        app.mount("/assets", StaticFiles(directory=str(assets)), name="monitor_assets")

    async def spa_root() -> FileResponse:
        if not index.is_file():
            raise HTTPException(
                status_code=503,
                detail="Monitor SPA index.html missing in CABLEGUARD_MONITOR_STATIC_DIR",
            )
        return FileResponse(index)

    async def spa_fallback(full_path: str) -> FileResponse:
        if _is_reserved(full_path):
            raise HTTPException(status_code=404, detail="Not found")
        candidate = (static_path / full_path).resolve()
        try:
            candidate.relative_to(static_path.resolve())
        except ValueError as exc:
            raise HTTPException(status_code=404, detail="Not found") from exc
        if candidate.is_file():
            return FileResponse(candidate)
        if not index.is_file():
            raise HTTPException(
                status_code=503,
                detail="Monitor SPA index.html missing in CABLEGUARD_MONITOR_STATIC_DIR",
            )
        return FileResponse(index)

    app.add_api_route(
        "/",
        spa_root,
        methods=["GET", "HEAD"],
        include_in_schema=False,
        name="monitor_spa_root",
    )
    app.add_api_route(
        "/{full_path:path}",
        spa_fallback,
        methods=["GET", "HEAD"],
        include_in_schema=False,
        name="monitor_spa_fallback",
    )
