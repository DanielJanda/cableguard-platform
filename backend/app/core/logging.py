import logging
import sys


def setup_logging() -> None:
    root = logging.getLogger()
    if root.handlers:
        return
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(
        logging.Formatter("%(asctime)s [%(levelname)s] %(name)s: %(message)s", "%H:%M:%S")
    )
    root.addHandler(handler)
    root.setLevel(logging.INFO)
    # Never attach API keys to log records elsewhere
    logging.getLogger("uvicorn.access").setLevel(logging.INFO)
