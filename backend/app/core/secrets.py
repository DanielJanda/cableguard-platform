"""Runtime secret validation – never log secret values."""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from app.core.config import Settings

PLACEHOLDER_SECRETS = frozenset(
    {
        "",
        "change-me-local-dev-key",
        "change-me-local-kiosk-key",
    }
)


def is_configured_secret(value: str | None) -> bool:
    if value is None:
        return False
    normalized = value.strip()
    if not normalized:
        return False
    return normalized not in PLACEHOLDER_SECRETS


def validate_runtime_secrets(settings: Settings) -> None:
    """Fail fast when required server-side secrets are missing or placeholders."""
    errors: list[str] = []
    if not is_configured_secret(settings.ingest_api_key):
        errors.append(
            "CABLEGUARD_INGEST_API_KEY is missing or uses a placeholder. "
            "Set a unique value in cableguard-platform/.env (see .env.example)."
        )
    if not is_configured_secret(settings.kiosk_api_key):
        errors.append(
            "CABLEGUARD_KIOSK_API_KEY is missing or uses a placeholder. "
            "Set a unique value in cableguard-platform/.env (see .env.example)."
        )
    if errors:
        raise RuntimeError("Event Core secret configuration error:\n- " + "\n- ".join(errors))
