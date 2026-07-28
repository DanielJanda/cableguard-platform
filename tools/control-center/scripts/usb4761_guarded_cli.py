#!/usr/bin/env python3
"""Guarded Advantech USB-4761 CLI for CableGuard Admin Studio.

Commands print JSON only (no secrets / no full serial).
Writes require explicit subcommands. Max pulse clamped to 250 ms.
No detector auto-control. DemoDevice is never used unless USB4761_ALLOW_DEMO=1.
"""
from __future__ import annotations

import argparse
import json
import os
import platform
import sys
import time

NUM_RELAYS = 8
NUM_DI = 8
MAX_PULSE_MS = 250


def _daqnavi_base() -> str | None:
    candidates = [
        os.environ.get("ADVANTECH_DAQNAVI_PATH"),
        r"C:\Advantech\DAQNavi",
        r"C:\Program Files\Advantech\DAQNavi",
        r"C:\Program Files (x86)\Advantech\DAQNavi",
    ]
    return next((p for p in candidates if p and os.path.isdir(p)), None)


def _find_assembly(base: str) -> tuple[str, str]:
    """Return (assembly_name, directory) preferring Automation.BDaq4."""
    for ver in ("4.0.0.0", "1.0.0.0"):
        for name in ("Automation.BDaq4", "Automation.BDaq"):
            d = os.path.join(base, "Automation.BDaq", ver)
            dll = os.path.join(d, f"{name}.dll")
            if os.path.isfile(dll):
                return name, d
    # flat layout fallback
    for name in ("Automation.BDaq4", "Automation.BDaq"):
        dll = os.path.join(base, f"{name}.dll")
        if os.path.isfile(dll):
            return name, base
    raise RuntimeError("Automation.BDaq / Automation.BDaq4.dll not found under DAQNavi")


def _load_bdaq():
    import clr  # type: ignore

    base = _daqnavi_base()
    if not base:
        raise RuntimeError("SDK NOT FOUND: DAQNavi directory missing")

    driver = os.path.join(base, "Driver")
    usb_amd64 = os.path.join(driver, "USB4761", "amd64")
    for p in (usb_amd64, driver):
        if os.path.isdir(p):
            os.environ["PATH"] = p + os.pathsep + os.environ.get("PATH", "")

    asm_name, asm_dir = _find_assembly(base)
    if asm_dir not in sys.path:
        sys.path.insert(0, asm_dir)
    if base not in sys.path:
        sys.path.insert(0, base)

    try:
        clr.AddReference(asm_name)
    except Exception as e:
        raise RuntimeError(f"SDK LOAD ERROR: {asm_name}: {e}") from e

    from Automation.BDaq import InstantDoCtrl, InstantDiCtrl, DeviceInformation  # type: ignore

    return InstantDoCtrl, InstantDiCtrl, DeviceInformation, base, asm_name


def _device_names() -> list[str]:
    names = ["USB-4761,BID#0", "USB-4761"]
    if os.environ.get("USB4761_ALLOW_DEMO") == "1":
        names.append("DemoDevice,BID#0")
    return names


def _open_do(InstantDoCtrl, DeviceInformation):
    last = None
    for desc in _device_names():
        try:
            ctrl = InstantDoCtrl()
            ctrl.SelectedDevice = DeviceInformation(desc)
            return ctrl, desc
        except Exception as e:
            last = e
            continue
    raise RuntimeError(f"OPEN FAILED: DO — {last}")


def _open_di(InstantDiCtrl, DeviceInformation):
    last = None
    for desc in _device_names():
        try:
            ctrl = InstantDiCtrl()
            ctrl.SelectedDevice = DeviceInformation(desc)
            return ctrl, desc
        except Exception as e:
            last = e
            continue
    raise RuntimeError(f"OPEN FAILED: DI — {last}")


def _write_bit(ctrl, channel_1based: int, on: bool) -> None:
    idx = int(channel_1based) - 1
    if not 0 <= idx < NUM_RELAYS:
        raise ValueError(f"channel out of range: {channel_1based}")
    port = idx // 4
    bit = idx % 4
    ctrl.WriteBit(port, bit, 1 if on else 0)


def _read_bits_do(ctrl) -> list[bool]:
    values = []
    for i in range(NUM_RELAYS):
        port = i // 4
        bit = i % 4
        try:
            values.append(bool(ctrl.ReadBit(port, bit)))
        except Exception:
            values.append(False)
    return values


def _read_bits_di(ctrl) -> list[bool]:
    values = []
    for i in range(NUM_DI):
        port = i // 8
        bit = i % 8
        try:
            values.append(bool(ctrl.ReadBit(port, bit)))
        except Exception:
            values.append(False)
    return values


def cmd_probe(_: argparse.Namespace) -> int:
    """Read-only probe: load SDK, open device, read DI/DO counts and values. No writes."""
    payload: dict = {
        "ok": False,
        "status": "UNKNOWN",
        "os": platform.platform(),
        "process_arch": platform.machine(),
        "python_arch": platform.architecture()[0],
        "sdk_path": None,
        "assembly": None,
        "model": "USB-4761",
        "di_count": 0,
        "do_count": 0,
        "di": [],
        "do": [],
        "device_desc": None,
        "error": None,
        "error_code": None,
    }
    try:
        InstantDoCtrl, InstantDiCtrl, DeviceInformation, base, asm = _load_bdaq()
        payload["sdk_path"] = base
        payload["assembly"] = asm

        do_ctrl, do_desc = _open_do(InstantDoCtrl, DeviceInformation)
        payload["device_desc"] = do_desc
        try:
            do_vals = _read_bits_do(do_ctrl)
            payload["do"] = do_vals
            payload["do_count"] = len(do_vals)
        except Exception as e:
            payload["status"] = "READ FAILED"
            payload["error"] = str(e)
            payload["error_code"] = "DO_READ"
            print(json.dumps(payload))
            return 4
        finally:
            do_ctrl.Dispose()

        try:
            di_ctrl, _ = _open_di(InstantDiCtrl, DeviceInformation)
            try:
                di_vals = _read_bits_di(di_ctrl)
                payload["di"] = di_vals
                payload["di_count"] = len(di_vals)
            finally:
                di_ctrl.Dispose()
        except Exception as e:
            payload["status"] = "READ FAILED"
            payload["error"] = str(e)
            payload["error_code"] = "DI_READ"
            print(json.dumps(payload))
            return 4

        payload["ok"] = True
        payload["status"] = "CONNECTED"
        print(json.dumps(payload))
        return 0
    except Exception as e:
        msg = str(e)
        if "SDK NOT FOUND" in msg:
            payload["status"] = "SDK NOT FOUND"
            payload["error_code"] = "SDK_NOT_FOUND"
        elif "SDK LOAD ERROR" in msg:
            payload["status"] = "SDK LOAD ERROR"
            payload["error_code"] = "SDK_LOAD"
        elif "OPEN FAILED" in msg:
            payload["status"] = "OPEN FAILED"
            payload["error_code"] = "OPEN_FAILED"
        elif "Architecture" in msg or "BadImage" in msg:
            payload["status"] = "ARCHITECTURE MISMATCH"
            payload["error_code"] = "ARCH"
        else:
            payload["status"] = "DRIVER ERROR"
            payload["error_code"] = "DRIVER"
        payload["error"] = msg
        print(json.dumps(payload))
        return 1


def cmd_discover(args: argparse.Namespace) -> int:
    # Alias: full probe semantics (no DemoDevice unless allowed).
    return cmd_probe(args)


def cmd_all_off(_: argparse.Namespace) -> int:
    try:
        InstantDoCtrl, _, DeviceInformation, _, _ = _load_bdaq()
        ctrl, _ = _open_do(InstantDoCtrl, DeviceInformation)
        try:
            for ch in range(1, NUM_RELAYS + 1):
                _write_bit(ctrl, ch, False)
        finally:
            ctrl.Dispose()
        print(json.dumps({"ok": True, "op": "all-off"}))
        return 0
    except Exception as e:
        print(json.dumps({"ok": False, "error": str(e)}))
        return 1


def cmd_pulse(args: argparse.Namespace) -> int:
    ms = max(50, min(int(args.ms), MAX_PULSE_MS))
    ch = int(args.channel)
    try:
        InstantDoCtrl, _, DeviceInformation, _, _ = _load_bdaq()
        ctrl, _ = _open_do(InstantDoCtrl, DeviceInformation)
        try:
            for c in range(1, NUM_RELAYS + 1):
                _write_bit(ctrl, c, False)
            _write_bit(ctrl, ch, True)
            time.sleep(ms / 1000.0)
            _write_bit(ctrl, ch, False)
        finally:
            ctrl.Dispose()
        print(json.dumps({"ok": True, "op": "pulse", "channel": ch, "ms": ms}))
        return 0
    except Exception as e:
        print(json.dumps({"ok": False, "error": str(e)}))
        return 1


def cmd_read_di(_: argparse.Namespace) -> int:
    try:
        _, InstantDiCtrl, DeviceInformation, _, _ = _load_bdaq()
        di, _ = _open_di(InstantDiCtrl, DeviceInformation)
        try:
            values = _read_bits_di(di)
        finally:
            di.Dispose()
        print(json.dumps({"ok": True, "di": values}))
        return 0
    except Exception as e:
        print(json.dumps({"ok": False, "error": str(e), "di": []}))
        return 1


def main() -> int:
    p = argparse.ArgumentParser(description="Guarded USB-4761 CLI")
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("probe", help="Read-only DI/DO probe (no writes)")
    sub.add_parser("discover")
    sub.add_parser("all-off")
    sub.add_parser("read-di")
    pulse = sub.add_parser("pulse")
    pulse.add_argument("--channel", type=int, required=True)
    pulse.add_argument("--ms", type=int, default=200)

    args = p.parse_args()
    if args.cmd in ("probe", "discover"):
        return cmd_probe(args)
    if args.cmd == "all-off":
        return cmd_all_off(args)
    if args.cmd == "pulse":
        return cmd_pulse(args)
    if args.cmd == "read-di":
        return cmd_read_di(args)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
