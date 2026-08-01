"""Incident media API and camera registry tests."""

from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from fastapi.testclient import TestClient

from app.core.config import get_settings
from app.db.models import Event, IncidentClipJob
from app.db.session import get_session_factory
from app.services.camera_registry import canonicalize_camera_id, mediamtx_path_for_camera
from app.services.incident_paths import incidents_root, resolve_safe_media_path


def test_canonicalize_office_aliases() -> None:
    assert canonicalize_camera_id("camera-122727") == "office-63"
    assert canonicalize_camera_id("office-test-camera") == "office-63"
    assert mediamtx_path_for_camera("office-63") == "office-test-camera"


def test_path_traversal_rejected(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("CABLEGUARD_INCIDENT_STORAGE_DIR", str(tmp_path / "incidents"))
    get_settings.cache_clear()
    root = incidents_root()
    (root / "ok").mkdir(parents=True)
    good = root / "ok" / "snapshot.jpg"
    good.write_bytes(b"jpeg")
    assert resolve_safe_media_path(stored_path=str(good), event_id="e1", kind="snapshot") == good.resolve()
    evil = tmp_path / "outside.jpg"
    evil.write_bytes(b"x")
    assert resolve_safe_media_path(stored_path=str(evil), event_id="e1", kind="snapshot") is None
    get_settings.cache_clear()


def test_ingest_normalizes_camera_and_exposes_media_status(
    client: TestClient, auth_headers: dict[str, str], monkeypatch
) -> None:
    monkeypatch.setenv("CABLEGUARD_INCIDENT_PIPELINE_ENABLED", "true")
    get_settings.cache_clear()
    event_id = str(uuid4())
    body = {
        "event_id": event_id,
        "event_type": "fall_risk_detected",
        "severity": "critical",
        "site_id": "office",
        "station_id": "office-test",
        "camera_id": "camera-122727",
        "service_id": "fall-office-test",
        "created_at": datetime.now(timezone.utc).isoformat(),
        "risk_score": 0.91,
        "payload_json": {"test_mode": True, "fall_episode_id": "ep-1"},
    }
    r = client.post("/api/v1/events", json=body, headers=auth_headers)
    assert r.status_code == 201, r.text
    data = r.json()
    assert data["camera_id"] == "office-63"
    assert data["snapshot_status"] == "PENDING"
    assert data["clip_status"] == "PENDING"

    # Media not ready yet
    snap = client.get(f"/api/v1/events/{event_id}/snapshot")
    assert snap.status_code == 409

    clip = client.get(f"/api/v1/events/{event_id}/clip")
    assert clip.status_code == 409

    # Path traversal via forged job path must 404
    db = get_session_factory()()
    try:
        job = db.query(IncidentClipJob).filter_by(event_id=event_id).one()
        job.status = "READY"
        job.final_path = str(Path("C:/Windows/System32/drivers/etc/hosts"))
        job.snapshot_path = str(Path("C:/Windows/System32/drivers/etc/hosts"))
        ev = db.query(Event).filter_by(event_id=event_id).one()
        ev.clip_status = "READY"
        ev.snapshot_status = "READY"
        db.commit()
    finally:
        db.close()

    bad = client.get(f"/api/v1/events/{event_id}/clip")
    assert bad.status_code == 404
    get_settings.cache_clear()


def test_range_playback_when_ready(
    client: TestClient, auth_headers: dict[str, str], tmp_path: Path, monkeypatch
) -> None:
    monkeypatch.setenv("CABLEGUARD_INCIDENT_PIPELINE_ENABLED", "false")
    monkeypatch.setenv("CABLEGUARD_INCIDENT_STORAGE_DIR", str(tmp_path / "incidents"))
    get_settings.cache_clear()

    event_id = str(uuid4())
    body = {
        "event_id": event_id,
        "event_type": "fall_risk_detected",
        "severity": "critical",
        "site_id": "office",
        "station_id": "office-test",
        "camera_id": "office-63",
        "service_id": "fall-office-test",
        "created_at": datetime.now(timezone.utc).isoformat(),
        "risk_score": 0.5,
        "payload_json": {"test_mode": True},
    }
    assert client.post("/api/v1/events", json=body, headers=auth_headers).status_code == 201

    root = incidents_root()
    day = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    media_dir = root / "office-63" / day / event_id
    media_dir.mkdir(parents=True)
    clip_path = media_dir / "incident.mp4"
    # Minimal fake mp4 bytes (not a real container — enough for Range headers)
    clip_path.write_bytes(b"0" * 4096)
    snap_path = media_dir / "snapshot.jpg"
    snap_path.write_bytes(b"\xff\xd8\xff" + b"0" * 100)

    now = datetime.now(timezone.utc)
    db = get_session_factory()()
    try:
        ev = db.query(Event).filter_by(event_id=event_id).one()
        ev.clip_status = "READY"
        ev.snapshot_status = "READY"
        ev.clip_url = f"/api/v1/events/{event_id}/clip"
        ev.snapshot_url = f"/api/v1/events/{event_id}/snapshot"
        db.add(
            IncidentClipJob(
                event_id=event_id,
                camera_id="office-63",
                mediamtx_path="office-test-camera",
                requested_start=now,
                requested_end=now,
                available_after=now,
                status="READY",
                attempts=1,
                final_path=str(clip_path.resolve()),
                snapshot_path=str(snap_path.resolve()),
                created_at=now,
                updated_at=now,
            )
        )
        db.commit()
    finally:
        db.close()

    full = client.get(f"/api/v1/events/{event_id}/clip")
    assert full.status_code == 200
    assert full.headers.get("accept-ranges") == "bytes"

    partial = client.get(
        f"/api/v1/events/{event_id}/clip",
        headers={"Range": "bytes=0-99"},
    )
    assert partial.status_code == 206
    assert len(partial.content) == 100
    assert partial.headers["content-range"].startswith("bytes 0-99/")

    snap = client.get(f"/api/v1/events/{event_id}/snapshot")
    assert snap.status_code == 200
    assert snap.headers["content-type"].startswith("image/jpeg")
    get_settings.cache_clear()
