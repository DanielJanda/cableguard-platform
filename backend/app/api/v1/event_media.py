"""Safe media serving for incident snapshot / clip — no filesystem paths exposed."""

from __future__ import annotations

import logging
import mimetypes

from fastapi import APIRouter, Depends, HTTPException, Request, status
from fastapi.responses import FileResponse, Response, StreamingResponse
from sqlalchemy import select
from sqlalchemy.orm import Session

from app.db.models import Event, IncidentClipJob
from app.db.session import get_db
from app.services.incident_paths import resolve_safe_media_path

logger = logging.getLogger(__name__)

router = APIRouter()


def _audit_access(event_id: str, kind: str, request: Request, ok: bool) -> None:
    client = request.client.host if request.client else "?"
    logger.info(
        "media_access event_id=%s kind=%s client=%s ok=%s",
        event_id,
        kind,
        client,
        ok,
    )


def _job_for_event(db: Session, event_id: str) -> IncidentClipJob | None:
    return db.scalar(select(IncidentClipJob).where(IncidentClipJob.event_id == event_id))


def _get_event_or_404(db: Session, event_id: str) -> Event:
    event = db.scalar(select(Event).where(Event.event_id == event_id))
    if event is None:
        raise HTTPException(status_code=404, detail="Event not found")
    return event


@router.get("/events/{event_id}/snapshot")
def get_event_snapshot(
    event_id: str,
    request: Request,
    db: Session = Depends(get_db),
) -> Response:
    event = _get_event_or_404(db, event_id)
    if event.snapshot_status == "PENDING":
        _audit_access(event_id, "snapshot", request, False)
        raise HTTPException(status_code=409, detail="Snapshot is still being prepared")
    if event.snapshot_status == "EXPIRED":
        _audit_access(event_id, "snapshot", request, False)
        raise HTTPException(status_code=410, detail="Snapshot expired by retention policy")
    if event.snapshot_status == "FAILED":
        _audit_access(event_id, "snapshot", request, False)
        raise HTTPException(status_code=404, detail="Snapshot unavailable")
    if event.snapshot_status not in ("READY",) and not event.snapshot_url:
        _audit_access(event_id, "snapshot", request, False)
        raise HTTPException(status_code=404, detail="Snapshot not available")

    job = _job_for_event(db, event_id)
    path = resolve_safe_media_path(
        stored_path=job.snapshot_path if job else None,
        event_id=event_id,
        kind="snapshot",
    )
    if path is None:
        _audit_access(event_id, "snapshot", request, False)
        raise HTTPException(status_code=404, detail="Snapshot not available")
    _audit_access(event_id, "snapshot", request, True)
    return FileResponse(
        path,
        media_type="image/jpeg",
        filename="snapshot.jpg",
        headers={"Cache-Control": "private, max-age=60"},
    )


def _parse_range(range_header: str | None, file_size: int) -> tuple[int, int] | None:
    if not range_header or not range_header.startswith("bytes="):
        return None
    spec = range_header.removeprefix("bytes=").strip()
    if "," in spec:
        # Multi-range not supported
        return None
    start_s, _, end_s = spec.partition("-")
    try:
        if start_s == "":
            # suffix range
            suffix = int(end_s)
            if suffix <= 0:
                return None
            start = max(file_size - suffix, 0)
            end = file_size - 1
        else:
            start = int(start_s)
            end = int(end_s) if end_s else file_size - 1
    except ValueError:
        return None
    if start < 0 or end < start or start >= file_size:
        return None
    end = min(end, file_size - 1)
    return start, end


@router.get("/events/{event_id}/clip")
def get_event_clip(
    event_id: str,
    request: Request,
    db: Session = Depends(get_db),
) -> Response:
    event = _get_event_or_404(db, event_id)
    if event.clip_status == "PENDING":
        _audit_access(event_id, "clip", request, False)
        raise HTTPException(status_code=409, detail="Video is still being prepared")
    if event.clip_status == "EXPIRED":
        _audit_access(event_id, "clip", request, False)
        raise HTTPException(status_code=410, detail="Video expired by retention policy")
    if event.clip_status == "FAILED":
        _audit_access(event_id, "clip", request, False)
        raise HTTPException(status_code=404, detail="Video unavailable")
    if event.clip_status != "READY":
        _audit_access(event_id, "clip", request, False)
        raise HTTPException(status_code=404, detail="Video not available")

    job = _job_for_event(db, event_id)
    path = resolve_safe_media_path(
        stored_path=job.final_path if job else None,
        event_id=event_id,
        kind="clip",
    )
    if path is None:
        _audit_access(event_id, "clip", request, False)
        raise HTTPException(status_code=404, detail="Video not available")

    file_size = path.stat().st_size
    max_response = 250_000_000
    if file_size > max_response:
        _audit_access(event_id, "clip", request, False)
        raise HTTPException(status_code=413, detail="Video too large")

    content_type = mimetypes.guess_type(str(path))[0] or "video/mp4"
    range_req = _parse_range(request.headers.get("range"), file_size)

    if range_req is None:
        _audit_access(event_id, "clip", request, True)
        return FileResponse(
            path,
            media_type=content_type,
            filename="incident.mp4",
            headers={
                "Accept-Ranges": "bytes",
                "Cache-Control": "private, max-age=60",
            },
        )

    start, end = range_req
    length = end - start + 1

    def iter_file() -> bytes:
        with path.open("rb") as f:
            f.seek(start)
            remaining = length
            chunk = 1024 * 256
            while remaining > 0:
                data = f.read(min(chunk, remaining))
                if not data:
                    break
                remaining -= len(data)
                yield data

    _audit_access(event_id, "clip", request, True)
    return StreamingResponse(
        iter_file(),
        status_code=status.HTTP_206_PARTIAL_CONTENT,
        media_type=content_type,
        headers={
            "Content-Range": f"bytes {start}-{end}/{file_size}",
            "Accept-Ranges": "bytes",
            "Content-Length": str(length),
            "Cache-Control": "private, max-age=60",
        },
    )
