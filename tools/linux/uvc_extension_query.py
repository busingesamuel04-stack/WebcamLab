#!/usr/bin/env python3
"""
WebcamLab Phase 12: Independent Linux UVC Control Investigation Tool
Target Hardware: Dell UZ2315H Monitor Integrated Webcam (VID 0BDA, PID 58BA)
Driver Interface: Linux uvcvideo kernel driver via UVCIOC_CTRL_QUERY ioctl

This tool performs strict read-only interrogation of UVC Extension Units (XUs)
specifically Unit 4 (Realtek Extended ISP Engine) and Unit 7 (Realtek Calibration & State Unit).
Zero writes or mutations are performed.
"""

import sys
import os
import glob
import ctypes
import fcntl
import struct
import errno

# UVC Control Query Request Types (UVC 1.1 Specification Section 4.2.2)
UVC_RC_UNDEFINED = 0x00
UVC_SET_CUR      = 0x01
UVC_GET_CUR      = 0x81
UVC_GET_MIN      = 0x82
UVC_GET_MAX      = 0x83
UVC_GET_RES      = 0x84
UVC_GET_LEN      = 0x85
UVC_GET_INFO     = 0x86
UVC_GET_DEF      = 0x87

# UVC Control Info Flags (from UVC_GET_INFO)
UVC_CONTROL_CAP_GET = (1 << 0)  # 0x01
UVC_CONTROL_CAP_SET = (1 << 1)  # 0x02
UVC_CONTROL_CAP_AUTOUPDATE = (1 << 3)
UVC_CONTROL_CAP_ASYNCHRONOUS = (1 << 4)

# Linux struct uvc_xu_control_query (linux/uvcvideo.h)
class UvcXuControlQuery(ctypes.Structure):
    _fields_ = [
        ("unit", ctypes.c_uint8),       # Extension Unit ID
        ("selector", ctypes.c_uint8),   # Control Selector ID
        ("query", ctypes.c_uint8),      # Request (UVC_GET_*)
        ("size", ctypes.c_uint16),      # Payload byte size
        ("data", ctypes.c_void_p),      # Pointer to user data buffer
    ]

def compute_ioctl_uvcioc_ctrl_query():
    # _IOWR('u', 0x21, struct uvc_xu_control_query)
    # _IOC(dir, type, nr, size)
    # dir: _IOC_READ | _IOC_WRITE = 3
    # type: 'u' = 0x75
    # nr: 0x21
    # size: sizeof(struct uvc_xu_control_query)
    ioc_read = 2
    ioc_write = 1
    ioc_dir = ioc_read | ioc_write
    ioc_type = ord('u')
    ioc_nr = 0x21
    ioc_size = ctypes.sizeof(UvcXuControlQuery)
    return (ioc_dir << 30) | (ioc_size << 16) | (ioc_type << 8) | ioc_nr

UVCIOC_CTRL_QUERY = compute_ioctl_uvcioc_ctrl_query()

# Target Dell UZ2315H Extension Unit Specifications
TARGET_UNITS = [
    {
        "id": 4,
        "name": "Unit 4 (Node 3, 1229a78c-47b4-4094-b0ce-db07386fb938)",
        "description": "Realtek Extended ISP Engine & Control Node",
        "selectors": [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]
    },
    {
        "id": 7,
        "name": "Unit 7 (Node 4, 0fb885c3-68c2-4547-90f7-8f47579d95fc)",
        "description": "Realtek ISP Calibration Header, Flag & Parameter Node",
        "selectors": [0, 1, 2, 3, 4, 5, 6]
    }
]

# Baseline payloads established in Phase 11 on Windows IKsControl
WINDOWS_PHASE11_BASELINE = {
    (4, 10): {"len": 8, "flags": "GET SET", "cur": "00 08 BB BB BB BB BB BB"},
    (4, 11): {"len": 0, "flags": "GET SET", "cur": "(Action / Trigger control, 0 bytes)"},
    (7, 1):  {"len": 9, "flags": "GET SET", "cur": "28 10 01 00 1B FF FF FF FF"},
    (7, 2):  {"len": 1, "flags": "GET SET", "cur": "01"},
    (7, 3):  {"len": 1, "flags": "GET SET", "cur": "00"},
    (7, 4):  {"len": 2, "flags": "GET SET", "cur": "00 00"}
}

def query_xu_control(fd, unit_id, selector, request, size):
    buf = (ctypes.c_uint8 * size)()
    query = UvcXuControlQuery()
    query.unit = unit_id
    query.selector = selector
    query.query = request
    query.size = size
    query.data = ctypes.cast(buf, ctypes.c_void_p)

    try:
        fcntl.ioctl(fd, UVCIOC_CTRL_QUERY, query)
        return bytes(buf), 0
    except OSError as e:
        return None, e.errno

def parse_info_flags(flags_byte):
    parts = []
    if flags_byte & UVC_CONTROL_CAP_GET:
        parts.append("GET")
    if flags_byte & UVC_CONTROL_CAP_SET:
        parts.append("SET")
    if flags_byte & UVC_CONTROL_CAP_AUTOUPDATE:
        parts.append("AUTOUPDATE")
    if flags_byte & UVC_CONTROL_CAP_ASYNCHRONOUS:
        parts.append("ASYNCH")
    return " ".join(parts) if parts else f"None (0x{flags_byte:02X})"

def find_dell_webcam_nodes():
    nodes = []
    for dev_path in sorted(glob.glob("/dev/video*")):
        sys_path = f"/sys/class/video4linux/{os.path.basename(dev_path)}/device"
        try:
            modalias_path = os.path.join(sys_path, "modalias")
            if os.path.exists(modalias_path):
                with open(modalias_path, "r") as f:
                    modalias = f.read().strip()
                if "v0BDAp58BA" in modalias.upper() or "0BDA:58BA" in modalias.upper():
                    nodes.append(dev_path)
                    continue

            name_path = os.path.join(sys_path, "name")
            if os.path.exists(name_path):
                with open(name_path, "r") as f:
                    name = f.read().strip()
                if "DELL" in name.upper() or "WEBCAM" in name.upper():
                    nodes.append(dev_path)
                    continue
        except Exception:
            pass

    if not nodes:
        nodes = sorted(glob.glob("/dev/video*"))
    return nodes

def investigate_device(dev_path):
    print("=" * 80)
    print(f"  WebcamLab Phase 12: Probing Device '{dev_path}'")
    print("=" * 80)

    try:
        fd = os.open(dev_path, os.O_RDWR)
    except Exception as e:
        print(f"  [ERROR] Cannot open {dev_path}: {e}")
        return

    try:
        for unit in TARGET_UNITS:
            u_id = unit["id"]
            u_name = unit["name"]
            u_desc = unit["description"]
            print(f"\n--- Interrogating {u_name} ---")
            print(f"    Description: {u_desc}")

            for sel in unit["selectors"]:
                # 1. Query Length (UVC_GET_LEN, size=2 bytes)
                raw_len, err_len = query_xu_control(fd, u_id, sel, UVC_GET_LEN, 2)
                if err_len != 0:
                    err_name = errno.errorcode.get(err_len, str(err_len))
                    if err_len in (errno.ENOENT, errno.EINVAL, errno.EPERM):
                        # Selector not supported or invalid
                        continue
                    print(f"  Selector {sel:2d}: GET_LEN failed (errno={err_name})")
                    continue

                reported_len = struct.unpack("<H", raw_len)[0]

                # 2. Query Info Flags (UVC_GET_INFO, size=1 byte)
                raw_info, err_info = query_xu_control(fd, u_id, sel, UVC_GET_INFO, 1)
                info_str = parse_info_flags(raw_info[0]) if err_info == 0 else f"Err ({err_info})"

                # 3. Query Current Value (UVC_GET_CUR)
                cur_str = "(None)"
                if reported_len > 0:
                    raw_cur, err_cur = query_xu_control(fd, u_id, sel, UVC_GET_CUR, reported_len)
                    if err_cur == 0 and raw_cur is not None:
                        cur_str = " ".join(f"{b:02X}" for b in raw_cur)
                    else:
                        cur_str = f"Error (errno={err_cur})"
                else:
                    cur_str = "(0 bytes / Action control)"

                # 4. Query Default Value (UVC_GET_DEF)
                def_str = "(None)"
                if reported_len > 0:
                    raw_def, err_def = query_xu_control(fd, u_id, sel, UVC_GET_DEF, reported_len)
                    if err_def == 0 and raw_def is not None:
                        def_str = " ".join(f"{b:02X}" for b in raw_def)
                    else:
                        def_str = f"Error (errno={err_def})"

                # Compare with Windows Phase 11 baseline
                baseline = WINDOWS_PHASE11_BASELINE.get((u_id, sel))
                match_status = "NEW / UNDOCUMENTED"
                if baseline:
                    match_len = (reported_len == baseline["len"])
                    match_flags = (baseline["flags"] in info_str)
                    match_cur = (cur_str == baseline["cur"] or baseline["len"] == 0)
                    if match_len and match_flags and match_cur:
                        match_status = "EXACT MATCH (100% Windows Parity)"
                    else:
                        match_status = f"DIVERGENCE (Win: len={baseline['len']}, cur={baseline['cur']})"

                print(f"  Selector {sel:2d}: Len={reported_len:2d}B | Flags=[{info_str}] | Status: {match_status}")
                print(f"               GET_CUR = {cur_str}")
                if def_str != "(None)":
                    print(f"               GET_DEF = {def_str}")

    finally:
        os.close(fd)

def main():
    print("=" * 80)
    print("  WebcamLab Phase 12: Independent Linux UVC Investigation")
    print("  Target: Realtek RTS5875 (0BDA:58BA) on Dell UZ2315H")
    print("  Strict Read-Only Mode: UVCIOC_CTRL_QUERY (GET_LEN, INFO, CUR, DEF)")
    print("=" * 80)

    nodes = find_dell_webcam_nodes()
    if not nodes:
        print("[!] No /dev/video* devices found. Is the webcam attached or forwarded to this Linux environment?")
        sys.exit(1)

    print(f"Found {len(nodes)} candidate video device node(s): {', '.join(nodes)}")
    for node in nodes:
        investigate_device(node)

if __name__ == "__main__":
    main()
