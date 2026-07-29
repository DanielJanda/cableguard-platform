"""Enable Gate 2 synthetic MediaMTX paths via API (no camera credentials)."""

from __future__ import annotations

import json
import shutil
import urllib.error
import urllib.request
from pathlib import Path

API = "http://127.0.0.1:9997"
FFMPEG = shutil.which("ffmpeg") or ""


def _post_path(name: str, lavfi: str) -> None:
    if not FFMPEG:
        raise SystemExit("ffmpeg not found on PATH")
    ff = FFMPEG.replace("\\", "/")
    run = (
        f"{ff} -hide_banner -loglevel error -re "
        f"-f lavfi -i {lavfi} "
        f"-c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p -g 50 -bf 0 -an "
        f"-f rtsp rtsp://127.0.0.1:8554/{name}"
    )
    body = json.dumps({"runOnInit": run, "runOnInitRestart": True}).encode()
    req = urllib.request.Request(
        f"{API}/v3/config/paths/add/{name}",
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            print(f"ADD {name} -> {resp.status}")
    except urllib.error.HTTPError as exc:
        if exc.code in (400, 409):
            print(f"EXISTS? {name} ({exc.code}) — try patch")
            req2 = urllib.request.Request(
                f"{API}/v3/config/paths/patch/{name}",
                data=body,
                headers={"Content-Type": "application/json"},
                method="PATCH",
            )
            with urllib.request.urlopen(req2, timeout=5) as resp:
                print(f"PATCH {name} -> {resp.status}")
        else:
            raise


def main() -> None:
    _post_path("office-test-grid-2", "testsrc2=size=1280x720:rate=25")
    _post_path("office-test-grid-3", "smptebars=size=1280x720:rate=25")
    print("Done. Paths are TEST/SYNTHETIC — restart not required for API-added paths.")


if __name__ == "__main__":
    main()
