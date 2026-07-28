#!/usr/bin/env python3
"""Guarded Advantech USB-4761 CLI for CableGuard Admin Studio.

Commands print JSON only (no secrets). Writes require explicit subcommands.
Max pulse is clamped to 500 ms. No detector auto-control.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time

NUM_RELAYS = 8
MAX_PULSE_MS = 500


def _load_bdaq():
    import clr  # type: ignore

    candidates = [
        os.environ.get("ADVANTECH_DAQNAVI_PATH"),
        r"C:\Advantech\DAQNavi",
        r"C:\Program Files\Advantech\DAQNavi",
        r"C:\Program Files (x86)\Advantech\DAQNavi",
    ]
    base = next((p for p in candidates if p and os.path.isdir(p)), None)
    if not base:
        raise RuntimeError("DAQNavi not found")

    driver = os.path.join(base, "Driver")
    if os.path.isdir(driver):
        os.environ["PATH"] = driver + os.pathsep + os.environ.get("PATH", "")

    sys.path.append(base)
    # Automation.BDaq lives under DAQNavi
    clr.AddReference("Automation.BDaq")
    from Automation.BDaq import InstantDoCtrl, InstantDiCtrl, DeviceInformation  # type: ignore

    return InstantDoCtrl, InstantDiCtrl, DeviceInformation


def _open_do(InstantDoCtrl, DeviceInformation):
    for desc in ("USB-4761,BID#0", "USB-4761", "DemoDevice,BID#0"):
        try:
            ctrl = InstantDoCtrl()
            ctrl.SelectedDevice = DeviceInformation(desc)
            return ctrl
        except Exception:
            continue
    raise RuntimeError("USB-4761 DO not found")


def _open_di(InstantDiCtrl, DeviceInformation):
    for desc in ("USB-4761,BID#0", "USB-4761", "DemoDevice,BID#0"):
        try:
            ctrl = InstantDiCtrl()
            ctrl.SelectedDevice = DeviceInformation(desc)
            return ctrl
        except Exception:
            continue
    return None


def _write_bit(ctrl, channel_1based: int, on: bool) -> None:
    idx = int(channel_1based) - 1
    if not 0 <= idx < NUM_RELAYS:
        raise ValueError(f"channel out of range: {channel_1based}")
    port = idx // 4
    bit = idx % 4
    ctrl.WriteBit(port, bit, 1 if on else 0)


def cmd_discover(_: argparse.Namespace) -> int:
    try:
        InstantDoCtrl, InstantDiCtrl, DeviceInformation = _load_bdaq()
        do = _open_do(InstantDoCtrl, DeviceInformation)
        di_ok = _open_di(InstantDiCtrl, DeviceInformation) is not None
        do.Dispose()
        print(json.dumps({"ok": True, "device": "USB-4761", "relays": NUM_RELAYS, "di_open": di_ok}))
        return 0
    except Exception as e:
        print(json.dumps({"ok": False, "error": str(e)}))
        return 1


def cmd_all_off(_: argparse.Namespace) -> int:
    try:
        InstantDoCtrl, _, DeviceInformation = _load_bdaq()
        ctrl = _open_do(InstantDoCtrl, DeviceInformation)
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
        InstantDoCtrl, _, DeviceInformation = _load_bdaq()
        ctrl = _open_do(InstantDoCtrl, DeviceInformation)
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
        InstantDoCtrl, InstantDiCtrl, DeviceInformation = _load_bdaq()
        di = _open_di(InstantDiCtrl, DeviceInformation)
        if di is None:
            print(json.dumps({"ok": True, "di": [], "note": "DI open failed"}))
            return 0
        values = []
        try:
            for i in range(8):
                port = i // 8
                bit = i % 8
                try:
                    values.append(bool(di.ReadBit(port, bit)))
                except Exception:
                    values.append(False)
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

    sub.add_parser("discover")
    sub.add_parser("all-off")
    sub.add_parser("read-di")
    pulse = sub.add_parser("pulse")
    pulse.add_argument("--channel", type=int, required=True)
    pulse.add_argument("--ms", type=int, default=200)

    args = p.parse_args()
    if args.cmd == "discover":
        return cmd_discover(args)
    if args.cmd == "all-off":
        return cmd_all_off(args)
    if args.cmd == "pulse":
        return cmd_pulse(args)
    if args.cmd == "read-di":
        return cmd_read_di(args)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
