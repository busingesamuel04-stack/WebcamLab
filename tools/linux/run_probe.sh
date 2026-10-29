#!/usr/bin/env bash
#
# WebcamLab Phase 12: Independent Linux UVC Investigation Runner
# Target Hardware: Dell UZ2315H Monitor Integrated Webcam (0BDA:58BA)
#

set -e

echo "=================================================================="
echo "  WebcamLab Phase 12: Linux UVC Extension Unit Investigation"
echo "=================================================================="

# Check if v4l-utils is available
if command -v v4l2-ctl &> /dev/null; then
    echo "[INFO] v4l2-ctl detected. Listing available V4L2 devices:"
    v4l2-ctl --list-devices
else
    echo "[NOTE] v4l2-ctl not found. Proceeding with direct /dev/video* query."
fi

# Detect Python 3
if command -v python3 &> /dev/null; then
    echo "[INFO] Running Python UVC Extension Query tool..."
    python3 "$(dirname "$0")/uvc_extension_query.py"
elif command -v gcc &> /dev/null; then
    echo "[INFO] Python 3 not found. Compiling native C tool..."
    gcc -O2 -Wall "$(dirname "$0")/uvc_xu_query.c" -o /tmp/uvc_xu_query
    /tmp/uvc_xu_query
else
    echo "[ERROR] Neither python3 nor gcc is installed in this Linux environment."
    exit 1
fi
