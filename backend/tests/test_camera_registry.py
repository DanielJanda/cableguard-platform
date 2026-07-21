from __future__ import annotations

import pytest

from app.cameras.registry import get_registry, load_registry


def test_registry_loads_without_credentials() -> None:
    registry = load_registry()
    assert registry.registry_version == 1
    assert len(registry.cameras) >= 3


def test_horni_camera_enabled_with_proxy_paths() -> None:
    horni = next(c for c in get_registry().cameras if c.camera_id == "zahradky-horni-stanice")
    assert horni.enabled is True
    assert horni.stream_name == "zahradky-horni-stanice"
    assert horni.rtsp_proxy_path == "rtsp://127.0.0.1:8554/zahradky-horni-stanice"
    assert horni.whep_path == "/zahradky-horni-stanice/whep"
    assert "@" not in horni.rtsp_proxy_path


def test_disabled_cameras_present() -> None:
    disabled = [c for c in get_registry().cameras if not c.enabled]
    ids = {c.camera_id for c in disabled}
    assert "zahradky-dolni-stanice" in ids
    assert "testovaci-kancelar-kamera-1" in ids


def test_registry_rejects_credentials(tmp_path) -> None:
    bad = tmp_path / "bad.yml"
    bad.write_text(
        """
registry_version: 1
cameras:
  - camera_id: bad
    site_id: zahradky
    station_id: horni_stanice
    stream_name: bad
    rtsp_proxy_path: rtsp://admin:secret@127.0.0.1:8554/bad
    whep_path: /bad/whep
    enabled: false
""",
        encoding="utf-8",
    )
    with pytest.raises(ValueError):
        load_registry(bad)
