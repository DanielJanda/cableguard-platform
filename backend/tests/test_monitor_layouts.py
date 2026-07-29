from __future__ import annotations

from fastapi.testclient import TestClient

from app.main import create_app


def test_monitor_layout_office_lab_three_camera():
    client = TestClient(create_app())
    r = client.get("/api/v1/monitor/layouts/office-lab-three-camera")
    assert r.status_code == 200
    body = r.json()
    assert body["layout_id"] == "office-lab-three-camera"
    assert body["layout_type"] == "3"
    assert body["lift_id"] == "office-lab"
    assert body["primary_camera_id"] == "office-63"
    assert len(body["cameras"]) == 3
    ids = [c["camera_id"] for c in body["cameras"]]
    assert ids == ["office-63", "office-test-2", "office-test-3"]
    for cam in body["cameras"]:
        assert "credential" not in str(cam).lower()
        assert "rtsp://" not in str(cam).lower()
        assert cam["whep_path"].startswith("/")
        assert cam["mediamtx_path"]
    # restraint not falsely READY
    restraint = [
        p
        for c in body["cameras"]
        for p in c["pipelines"]
        if p["detector_type"] == "restraint"
    ]
    assert restraint
    assert all(p["state"] in ("NOT_IMPLEMENTED", "NOT_CONFIGURED") for p in restraint)


def test_monitor_layout_unknown_404():
    client = TestClient(create_app())
    r = client.get("/api/v1/monitor/layouts/does-not-exist")
    assert r.status_code == 404


def test_monitor_layouts_list():
    client = TestClient(create_app())
    r = client.get("/api/v1/monitor/layouts")
    assert r.status_code == 200
    assert "office-lab-three-camera" in r.json()
