"""CableGuard Platform FastAPI application."""

from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.api import bff
from app.api.v1 import acknowledgements, events, heartbeats, status, websocket
from app.core.config import get_settings
from app.core.logging import setup_logging
from app.core.secrets import validate_runtime_secrets
from app.db.session import init_engine
from app.services.status_monitor import StatusMonitor
from app.web.monitor_ui import mount_monitor_ui


@asynccontextmanager
async def lifespan(app: FastAPI):
    setup_logging()
    settings = get_settings()
    if settings.env != "test":
        validate_runtime_secrets(settings)
    init_engine(settings.database_url)
    monitor = StatusMonitor(timeout_sec=settings.heartbeat_timeout_sec)
    app.state.monitor = monitor
    await monitor.start()
    yield
    await monitor.stop()
    proxy_client = getattr(app.state, "monitor_ui_client", None)
    if proxy_client is not None:
        await proxy_client.aclose()


def create_app() -> FastAPI:
    settings = get_settings()
    app = FastAPI(
        title="CableGuard Platform",
        version="0.1.0",
        lifespan=lifespan,
    )
    app.add_middleware(
        CORSMiddleware,
        allow_origins=settings.cors_origins_list,
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    app.include_router(events.router, prefix="/api/v1", tags=["events"])
    app.include_router(heartbeats.router, prefix="/api/v1", tags=["heartbeats"])
    app.include_router(acknowledgements.router, prefix="/api/v1", tags=["acknowledgements"])
    app.include_router(status.router, prefix="/api/v1", tags=["status"])
    app.include_router(websocket.router, tags=["websocket"])
    app.include_router(bff.router)
    # Monitor UI catch-all must be registered last.
    mount_monitor_ui(app, settings)
    return app


app = create_app()
