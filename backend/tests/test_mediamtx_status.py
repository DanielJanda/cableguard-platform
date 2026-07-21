from __future__ import annotations

from app.services import mediamtx_status


def test_map_status_when_api_offline() -> None:
    mapped = mediamtx_status.map_path_status(None, api_online=False)
    assert mapped["status"] == "mediamtx_offline"
    assert mapped["mediamtx_online"] is False


def test_map_status_when_path_missing() -> None:
    mapped = mediamtx_status.map_path_status(None, api_online=True)
    assert mapped["status"] == "path_not_found"


def test_map_status_when_ready_with_readers() -> None:
    mapped = mediamtx_status.map_path_status(
        {"ready": True, "readers": [{"id": "a"}], "source": {"ready": True}},
        api_online=True,
    )
    assert mapped["status"] == "streaming"
    assert mapped["readers"] == 1


def test_map_status_when_source_not_ready() -> None:
    mapped = mediamtx_status.map_path_status(
        {"ready": False, "readers": [], "source": {"ready": False}},
        api_online=True,
    )
    assert mapped["status"] == "source_not_ready"
