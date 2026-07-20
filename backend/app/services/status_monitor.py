from __future__ import annotations

import asyncio
import logging

from app.db import session as db_session
from app.services import health_service

logger = logging.getLogger(__name__)


class StatusMonitor:
    def __init__(self, timeout_sec: float) -> None:
        self.timeout_sec = timeout_sec
        self._task: asyncio.Task | None = None
        self._stop = asyncio.Event()

    async def start(self) -> None:
        self._stop.clear()
        self._task = asyncio.create_task(self._loop())

    async def stop(self) -> None:
        self._stop.set()
        if self._task:
            try:
                await asyncio.wait_for(self._task, timeout=5.0)
            except asyncio.TimeoutError:
                self._task.cancel()
                try:
                    await self._task
                except asyncio.CancelledError:
                    pass

    async def _loop(self) -> None:
        while not self._stop.is_set():
            try:
                await self.tick()
            except Exception:
                logger.exception("status monitor tick failed")
            try:
                await asyncio.wait_for(self._stop.wait(), timeout=1.0)
            except asyncio.TimeoutError:
                pass

    async def tick(self) -> None:
        factory = db_session.SessionLocal
        if factory is None:
            return
        db = factory()
        try:
            changed = health_service.mark_offline_if_stale(db, timeout_sec=self.timeout_sec)
            for row in changed:
                await health_service.publish_service_update(row, offline=True)
        finally:
            db.close()
