"""CableGuard Platform FastAPI application."""

from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.api.v1 import acknowledgements, events, heartbeats, status
from app.core.config import get_settings
from app.core.logging import setup_logging
from app.db.session import init_engine


@asynccontextmanager
async def lifespan(app: FastAPI):
    setup_logging()
    settings = get_settings()
    init_engine(settings.database_url)
    yield


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
    return app


app = create_app()
