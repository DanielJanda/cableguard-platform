"""Load monitor layouts from tracked fixtures (safe fields only)."""

from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

from fastapi import HTTPException

from app.schemas.monitor import MonitorLayoutResponse

DATA_DIR = Path(__file__).resolve().parents[1] / "data" / "monitor_layouts"


@lru_cache(maxsize=1)
def _load_all() -> dict[str, MonitorLayoutResponse]:
    layouts: dict[str, MonitorLayoutResponse] = {}
    if not DATA_DIR.is_dir():
        return layouts
    for path in sorted(DATA_DIR.glob("*.json")):
        raw = json.loads(path.read_text(encoding="utf-8"))
        model = MonitorLayoutResponse.model_validate(raw)
        layouts[model.layout_id] = model
    return layouts


def get_layout(layout_id: str) -> MonitorLayoutResponse:
    layouts = _load_all()
    layout = layouts.get(layout_id)
    if layout is None:
        raise HTTPException(status_code=404, detail=f"Unknown layout_id: {layout_id}")
    return layout


def list_layout_ids() -> list[str]:
    return sorted(_load_all().keys())
