"""Camera identity normalization and MediaMTX path mapping for incident clips."""

from __future__ import annotations

# Canonical Monitor tile camera_id -> MediaMTX path name
CAMERA_TO_MEDIAMTX_PATH: dict[str, str] = {
    "office-63": "office-test-camera",
    "office-test-camera": "office-test-camera",
    "camera-122727": "office-test-camera",
    "office-test-2": "office-test-2",
    "office-test-3": "office-test-3",
    "office-test-4": "office-test-4",
}

# Legacy / path aliases -> canonical Monitor camera_id
CAMERA_ID_ALIASES: dict[str, str] = {
    "office-test-camera": "office-63",
    "camera-122727": "office-63",
    "office-63": "office-63",
    "office-test-2": "office-test-2",
    "office-test-3": "office-test-3",
    "office-test-4": "office-test-4",
}


def canonicalize_camera_id(camera_id: str | None) -> str | None:
    if camera_id is None:
        return None
    raw = camera_id.strip()
    if not raw:
        return None
    return CAMERA_ID_ALIASES.get(raw, raw)


def mediamtx_path_for_camera(camera_id: str | None) -> str | None:
    canonical = canonicalize_camera_id(camera_id)
    if canonical is None:
        return None
    return CAMERA_TO_MEDIAMTX_PATH.get(canonical) or CAMERA_TO_MEDIAMTX_PATH.get(
        (camera_id or "").strip()
    )


def camera_ids_equivalent(a: str | None, b: str | None) -> bool:
    ca = canonicalize_camera_id(a)
    cb = canonicalize_camera_id(b)
    if ca is None or cb is None:
        return False
    return ca == cb
