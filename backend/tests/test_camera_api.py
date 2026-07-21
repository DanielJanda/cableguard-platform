from __future__ import annotations

from fastapi.testclient import TestClient


def test_list_cameras(client: TestClient) -> None:
    response = client.get("/api/v1/cameras")
    assert response.status_code == 200
    body = response.json()
    assert body["total"] >= 3
    horni = next(c for c in body["items"] if c["camera_id"] == "zahradky-horni-stanice")
    assert horni["enabled"] is True
    assert "@" not in horni["rtsp_proxy_path"]


def test_list_enabled_cameras(client: TestClient) -> None:
    response = client.get("/api/v1/cameras", params={"enabled_only": True})
    assert response.status_code == 200
    assert all(item["enabled"] for item in response.json()["items"])


def test_stream_status_endpoint(client: TestClient) -> None:
    response = client.get("/api/v1/streams/zahradky-horni-stanice/status")
    assert response.status_code == 200
    body = response.json()
    assert body["stream_name"] == "zahradky-horni-stanice"
    assert body["status"] in {
        "streaming",
        "ready",
        "source_not_ready",
        "path_not_found",
        "mediamtx_offline",
    }
