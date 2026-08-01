"""Async MediaMTX playback → incident MP4 + snapshot worker.

Does not block Event Core ingest or WebSocket alarm delivery.
Playback is localhost-only. Detector never encodes video.
"""

from __future__ import annotations

import asyncio
import json
import logging
import shutil
import subprocess
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

from sqlalchemy import or_, select

from app.core.config import get_settings
from app.core.datetime_utils import ensure_utc, serialize_utc_datetime
from app.db.models import Event, IncidentClipJob
from app.db.session import get_session_factory
from app.schemas.events import EventRead
from app.services.camera_registry import canonicalize_camera_id, mediamtx_path_for_camera
from app.services.incident_paths import (
    atomic_replace,
    event_incident_dir,
    sha256_file,
    write_json_sidecar,
)
from app.services.websocket_manager import ws_manager

logger = logging.getLogger(__name__)

JOB_PENDING = "PENDING"
JOB_READY = "READY"
JOB_FAILED = "FAILED"
MEDIA_PENDING = "PENDING"
MEDIA_READY = "READY"
MEDIA_FAILED = "FAILED"
MEDIA_NOT_REQUESTED = "NOT_REQUESTED"

FAILED_RECORDING_NOT_AVAILABLE = "FAILED_RECORDING_NOT_AVAILABLE"
FAILED_STORAGE = "FAILED_STORAGE"
FAILED_INVALID_MEDIA = "FAILED_INVALID_MEDIA"


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


def media_api_urls(event_id: str) -> tuple[str, str]:
    return (
        f"/api/v1/events/{event_id}/snapshot",
        f"/api/v1/events/{event_id}/clip",
    )


def enqueue_incident_jobs_for_event(event: Event) -> IncidentClipJob | None:
    """Create clip job when a fall event is newly persisted. Idempotent."""
    settings = get_settings()
    if not settings.incident_pipeline_enabled:
        return None
    if event.event_type != "fall_risk_detected":
        return None

    session_factory = get_session_factory()
    db = session_factory()
    try:
        existing = db.scalar(
            select(IncidentClipJob).where(IncidentClipJob.event_id == event.event_id)
        )
        if existing is not None:
            return existing

        camera_id = canonicalize_camera_id(event.camera_id) or (event.camera_id or "unknown")
        path = mediamtx_path_for_camera(camera_id)
        if not path:
            event.snapshot_status = MEDIA_FAILED
            event.clip_status = MEDIA_FAILED
            event.updated_at = _utcnow()
            db.merge(event)
            db.commit()
            logger.warning(
                "No MediaMTX path mapping for camera_id=%s event=%s",
                camera_id,
                event.event_id,
            )
            return None

        occurred = ensure_utc(event.created_at)
        pre = int(settings.pre_event_seconds)
        post = int(settings.post_event_seconds)
        start = occurred - timedelta(seconds=pre)
        end = occurred + timedelta(seconds=post)
        available_after = end + timedelta(seconds=2)
        now = _utcnow()

        job = IncidentClipJob(
            event_id=event.event_id,
            camera_id=camera_id,
            mediamtx_path=path,
            requested_start=start,
            requested_end=end,
            available_after=available_after,
            status=JOB_PENDING,
            attempts=0,
            next_attempt_at=available_after,
            created_at=now,
            updated_at=now,
        )
        row = db.get(Event, event.id) if event.id else None
        if row is None:
            row = db.scalar(select(Event).where(Event.event_id == event.event_id))
        if row is not None:
            row.camera_id = camera_id
            row.snapshot_status = MEDIA_PENDING
            row.clip_status = MEDIA_PENDING
            row.updated_at = now
        db.add(job)
        db.commit()
        db.refresh(job)
        logger.info(
            "Enqueued incident clip job event=%s camera=%s path=%s window=%ss+%ss",
            event.event_id,
            camera_id,
            path,
            pre,
            post,
        )
        return job
    except Exception:
        db.rollback()
        logger.exception("Failed to enqueue incident job for %s", event.event_id)
        return None
    finally:
        db.close()


def _find_tool(configured: str, fallback_names: list[str]) -> str | None:
    if configured and shutil.which(configured):
        return configured
    for name in fallback_names:
        found = shutil.which(name)
        if found:
            return found
    # Common Windows WinGet FFmpeg location pattern (best-effort)
    winget = Path.home() / "AppData/Local/Microsoft/WinGet/Packages"
    if winget.is_dir():
        for pattern in ("**/ffprobe.exe", "**/ffmpeg.exe"):
            matches = list(winget.glob(pattern))
            if matches and fallback_names[0].startswith("ffprobe"):
                for m in matches:
                    if m.name.lower().startswith("ffprobe"):
                        return str(m)
            if matches and fallback_names[0].startswith("ffmpeg"):
                for m in matches:
                    if m.name.lower().startswith("ffmpeg"):
                        return str(m)
    return None


def _ffprobe_info(ffprobe: str, path: Path) -> dict[str, Any]:
    cmd = [
        ffprobe,
        "-v",
        "error",
        "-show_entries",
        "format=duration,size:stream=codec_name,codec_type,width,height",
        "-of",
        "json",
        str(path),
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30, check=False)
    if proc.returncode != 0:
        raise RuntimeError(f"ffprobe failed: {(proc.stderr or '')[:200]}")
    return json.loads(proc.stdout or "{}")


def _download_playback(
    *,
    base_url: str,
    mediamtx_path: str,
    start: datetime,
    duration_sec: float,
    dest: Path,
) -> None:
    start_rfc = ensure_utc(start).strftime("%Y-%m-%dT%H:%M:%SZ")
    query = urllib.parse.urlencode(
        {
            "path": mediamtx_path,
            "start": start_rfc,
            "duration": f"{duration_sec:.3f}",
            "format": "mp4",
        }
    )
    url = f"{base_url.rstrip('/')}/get?{query}"
    # Hard safety: only localhost playback
    parsed = urllib.parse.urlparse(url)
    host = (parsed.hostname or "").lower()
    if host not in ("127.0.0.1", "localhost", "::1"):
        raise RuntimeError("MediaMTX playback must be localhost-only")

    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".download")
    try:
        req = urllib.request.Request(url, method="GET")
        with urllib.request.urlopen(req, timeout=120) as resp:  # noqa: S310 — localhost only
            status = getattr(resp, "status", 200)
            if status != 200:
                raise RuntimeError(f"playback HTTP {status}")
            with tmp.open("wb") as out:
                shutil.copyfileobj(resp, out)
        atomic_replace(tmp, dest)
    finally:
        if tmp.exists():
            try:
                tmp.unlink()
            except OSError:
                pass


def _remux_faststart(ffmpeg: str, src: Path, dest: Path) -> None:
    tmp = dest.with_name(dest.stem + ".remux.tmp.mp4")
    cmd = [
        ffmpeg,
        "-y",
        "-i",
        str(src),
        "-c",
        "copy",
        "-movflags",
        "+faststart",
        str(tmp),
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=120, check=False)
    if proc.returncode != 0 or not tmp.exists():
        err = (proc.stderr or proc.stdout or "")[-300:]
        raise RuntimeError(f"{FAILED_INVALID_MEDIA}:remux:{err}")
    atomic_replace(tmp, dest)


def _extract_snapshot(ffmpeg: str, clip: Path, dest: Path, at_sec: float, quality: int) -> None:
    # Use *.tmp.jpg (not *.jpg.tmp) — Windows ffmpeg image2 rejects .jpg.tmp patterns.
    tmp = dest.with_name(dest.stem + ".tmp.jpg")
    q = max(2, min(31, int(round((100 - quality) / 3.5)) or 2))
    cmd = [
        ffmpeg,
        "-y",
        "-ss",
        f"{max(0.0, at_sec):.3f}",
        "-i",
        str(clip),
        "-frames:v",
        "1",
        "-q:v",
        str(q),
        "-update",
        "1",
        str(tmp),
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=60, check=False)
    if proc.returncode != 0 or not tmp.exists():
        err = (proc.stderr or proc.stdout or "")[-300:]
        raise RuntimeError(f"{FAILED_INVALID_MEDIA}:snapshot:{err}")
    atomic_replace(tmp, dest)


async def _broadcast_event_updated(event: Event) -> None:
    data = EventRead.model_validate(event).model_dump(mode="json", exclude={"payload_json"})
    await ws_manager.broadcast(ws_manager.envelope("event.updated", data))
    if event.clip_status == MEDIA_READY:
        await ws_manager.broadcast(
            ws_manager.envelope(
                "clip_ready",
                {"event_id": event.event_id, "clip_status": event.clip_status},
            )
        )
    if event.snapshot_status == MEDIA_READY:
        await ws_manager.broadcast(
            ws_manager.envelope(
                "snapshot_ready",
                {"event_id": event.event_id, "snapshot_status": event.snapshot_status},
            )
        )


def _process_job(job_id: int) -> Event | None:
    settings = get_settings()
    session_factory = get_session_factory()
    db = session_factory()
    updated_event: Event | None = None
    try:
        job = db.get(IncidentClipJob, job_id)
        if job is None or job.status != JOB_PENDING:
            return None
        now = _utcnow()
        if job.next_attempt_at and ensure_utc(job.next_attempt_at) > now:
            return None
        if ensure_utc(job.available_after) > now:
            job.next_attempt_at = ensure_utc(job.available_after)
            job.updated_at = now
            db.commit()
            return None

        event = db.scalar(select(Event).where(Event.event_id == job.event_id))
        if event is None:
            job.status = JOB_FAILED
            job.last_error = "event_missing"
            job.updated_at = now
            db.commit()
            return None

        job.attempts += 1
        job.updated_at = now
        db.commit()

        ffmpeg = _find_tool(settings.ffmpeg_path, ["ffmpeg", "ffmpeg.exe"])
        ffprobe = _find_tool(settings.ffprobe_path, ["ffprobe", "ffprobe.exe"])
        if not ffmpeg or not ffprobe:
            raise RuntimeError(FAILED_STORAGE + ":ffmpeg_missing")

        occurred = ensure_utc(event.created_at)
        out_dir = event_incident_dir(job.camera_id, occurred, job.event_id)
        raw_tmp = out_dir / "incident.download.mp4"
        final_clip = out_dir / "incident.mp4"
        snapshot = out_dir / "snapshot.jpg"
        meta_path = out_dir / "metadata.json"

        duration = (ensure_utc(job.requested_end) - ensure_utc(job.requested_start)).total_seconds()
        try:
            _download_playback(
                base_url=settings.mediamtx_playback_base_url,
                mediamtx_path=job.mediamtx_path,
                start=ensure_utc(job.requested_start),
                duration_sec=duration,
                dest=raw_tmp,
            )
        except urllib.error.HTTPError as exc:
            if exc.code in (404, 400):
                raise RuntimeError(f"{FAILED_RECORDING_NOT_AVAILABLE}:HTTP_{exc.code}") from exc
            raise
        except urllib.error.URLError as exc:
            raise RuntimeError(f"{FAILED_RECORDING_NOT_AVAILABLE}:{type(exc).__name__}") from exc

        size = raw_tmp.stat().st_size
        if size < settings.clip_min_bytes:
            raise RuntimeError(f"{FAILED_INVALID_MEDIA}:too_small:{size}")
        if size > settings.clip_max_bytes:
            raise RuntimeError(f"{FAILED_INVALID_MEDIA}:too_large:{size}")

        _remux_faststart(ffmpeg, raw_tmp, final_clip)
        try:
            raw_tmp.unlink(missing_ok=True)
        except OSError:
            pass

        info = _ffprobe_info(ffprobe, final_clip)
        fmt = info.get("format") or {}
        actual_dur = float(fmt.get("duration") or 0.0)
        streams = info.get("streams") or []
        video = next((s for s in streams if s.get("codec_type") == "video"), None)
        if video is None:
            raise RuntimeError(f"{FAILED_INVALID_MEDIA}:no_video")
        codec = str(video.get("codec_name") or "")
        width = int(video.get("width") or 0)
        height = int(video.get("height") or 0)
        if codec.lower() not in ("h264", "avc1", "hev1", "h265", "hevc"):
            raise RuntimeError(f"{FAILED_INVALID_MEDIA}:codec:{codec}")
        if abs(actual_dur - duration) > float(settings.clip_duration_tolerance_sec) + 1.0:
            # Accept mild segment boundary skew; hard fail on gross mismatch
            if abs(actual_dur - duration) > max(10.0, duration * 0.5):
                raise RuntimeError(
                    f"{FAILED_INVALID_MEDIA}:duration:{actual_dur:.2f}!={duration:.2f}"
                )
            logger.warning(
                "Clip duration tolerance event=%s requested=%.2f actual=%.2f",
                job.event_id,
                duration,
                actual_dur,
            )

        snap_at = float(settings.pre_event_seconds)
        _extract_snapshot(
            ffmpeg,
            final_clip,
            snapshot,
            at_sec=snap_at,
            quality=int(settings.snapshot_jpeg_quality),
        )
        if snapshot.stat().st_size > settings.snapshot_max_bytes:
            raise RuntimeError(f"{FAILED_INVALID_MEDIA}:snapshot_too_large")

        clip_hash = sha256_file(final_clip)
        snap_hash = sha256_file(snapshot)
        snap_url, clip_url = media_api_urls(job.event_id)

        meta = {
            "event_id": job.event_id,
            "camera_id": job.camera_id,
            "mediamtx_path": job.mediamtx_path,
            "requested_start": serialize_utc_datetime(job.requested_start),
            "requested_end": serialize_utc_datetime(job.requested_end),
            "actual_duration": actual_dur,
            "requested_duration": duration,
            "codec": codec,
            "width": width,
            "height": height,
            "file_size": final_clip.stat().st_size,
            "sha256": clip_hash,
            "snapshot_sha256": snap_hash,
            "created_at": serialize_utc_datetime(_utcnow()),
        }
        write_json_sidecar(meta_path, meta)

        job.status = JOB_READY
        job.final_path = str(final_clip.resolve())
        job.snapshot_path = str(snapshot.resolve())
        job.clip_sha256 = clip_hash
        job.snapshot_sha256 = snap_hash
        job.actual_duration_sec = actual_dur
        job.last_error = None
        job.temp_path = None
        job.updated_at = _utcnow()

        event.snapshot_status = MEDIA_READY
        event.clip_status = MEDIA_READY
        event.snapshot_url = snap_url
        event.clip_url = clip_url
        event.camera_id = job.camera_id
        event.updated_at = job.updated_at
        db.commit()
        db.refresh(event)
        updated_event = event
        logger.info(
            "Incident clip READY event=%s duration=%.2fs size=%s",
            job.event_id,
            actual_dur,
            final_clip.stat().st_size,
        )
        return updated_event
    except Exception as exc:
        db.rollback()
        db = session_factory()
        try:
            job = db.get(IncidentClipJob, job_id)
            event = (
                db.scalar(select(Event).where(Event.event_id == job.event_id)) if job else None
            )
            if job is None:
                return None
            err = str(exc)[:500]
            job.last_error = err
            job.updated_at = _utcnow()
            settings = get_settings()
            if job.attempts >= settings.clip_max_attempts or err.startswith(
                (FAILED_INVALID_MEDIA, FAILED_STORAGE)
            ):
                job.status = JOB_FAILED
                if event is not None:
                    if event.clip_status != MEDIA_READY:
                        event.clip_status = MEDIA_FAILED
                    if event.snapshot_status != MEDIA_READY:
                        event.snapshot_status = MEDIA_FAILED
                    event.updated_at = job.updated_at
                    updated_event = event
                logger.error("Incident clip FAILED event=%s err=%s", job.event_id, err)
            else:
                backoff = settings.clip_retry_backoff_sec * (2 ** max(0, job.attempts - 1))
                job.next_attempt_at = _utcnow() + timedelta(seconds=min(backoff, 120))
                job.status = JOB_PENDING
                logger.warning(
                    "Incident clip retry event=%s attempt=%s next=%s err=%s",
                    job.event_id,
                    job.attempts,
                    job.next_attempt_at,
                    err,
                )
            db.commit()
            if updated_event is not None:
                db.refresh(updated_event)
            return updated_event
        finally:
            db.close()
    finally:
        db.close()


class IncidentClipWorker:
    def __init__(self) -> None:
        self._task: asyncio.Task[None] | None = None
        self._stop = asyncio.Event()

    async def start(self) -> None:
        settings = get_settings()
        if not settings.incident_pipeline_enabled:
            logger.info("Incident clip worker disabled")
            return
        self._stop.clear()
        self._task = asyncio.create_task(self._run(), name="incident-clip-worker")
        logger.info("Incident clip worker started")

    async def stop(self) -> None:
        self._stop.set()
        if self._task is not None:
            try:
                await asyncio.wait_for(self._task, timeout=10)
            except (asyncio.TimeoutError, asyncio.CancelledError):
                self._task.cancel()
            self._task = None
        logger.info("Incident clip worker stopped")

    async def _run(self) -> None:
        settings = get_settings()
        while not self._stop.is_set():
            try:
                await self._tick()
            except Exception:
                logger.exception("Incident clip worker tick failed")
            try:
                await asyncio.wait_for(self._stop.wait(), timeout=settings.clip_worker_poll_sec)
            except asyncio.TimeoutError:
                continue

    async def _tick(self) -> None:
        session_factory = get_session_factory()
        db = session_factory()
        try:
            now = _utcnow()
            jobs = list(
                db.scalars(
                    select(IncidentClipJob)
                    .where(IncidentClipJob.status == JOB_PENDING)
                    .where(
                        or_(
                            IncidentClipJob.next_attempt_at.is_(None),
                            IncidentClipJob.next_attempt_at <= now,
                        )
                    )
                    .order_by(IncidentClipJob.created_at.asc())
                    .limit(3)
                ).all()
            )
            job_ids = [j.id for j in jobs]
        finally:
            db.close()

        for job_id in job_ids:
            updated = await asyncio.to_thread(_process_job, job_id)
            if updated is not None:
                try:
                    await _broadcast_event_updated(updated)
                except Exception:
                    logger.exception("Failed to broadcast event.updated")


incident_clip_worker = IncidentClipWorker()
