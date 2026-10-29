# WebcamLab Linux UVC Extension Unit Investigation Tooling

This directory contains diagnostic utilities for interrogating the **Dell UZ2315H Integrated Webcam (`VID_0BDA&PID_58BA`, Realtek RTS5875 ISP)** under Linux using the standard Linux kernel UVC driver (`uvcvideo`) and the `UVCIOC_CTRL_QUERY` ioctl.

---

## 1. WSL2 / USBIP Device Forwarding Workflow

To forward the Dell webcam from Windows to a Linux WSL2 distribution:

### Host Prerequisites
1. **Windows Subsystem for Linux (WSL2)**:
   Installed via elevated command prompt:
   ```powershell
   wsl --install
   ```
2. **USBIPD-WIN**:
   Installed via winget (requires Administrator elevation):
   ```powershell
   winget install --id dorssel.usbipd-win
   ```

### Device Attachment Procedure
1. Identify the webcam bus ID:
   ```powershell
   usbipd list
   # Locate: "0bda:58ba  Dell Monitor Integrated Webcam" (e.g. Bus ID 2-4)
   ```
2. Bind the device (first time only, elevated Administrator prompt):
   ```powershell
   usbipd bind --busid <BUSID>
   ```
3. Attach to WSL2:
   ```powershell
   usbipd attach --wsl --busid <BUSID>
   ```

### CRITICAL OPERATIONAL CONSTRAINT: Windows Lockout
> [!IMPORTANT]
> When `usbipd attach --wsl` is active, Windows detaches the camera device node from `usbvideo.sys`.
> **Windows applications (including WebcamLab and the Windows Camera app) cannot access the camera while it is attached to WSL2.**
> Attempting to stream or capture under Windows will fail with `ERROR_DEVICE_NOT_CONNECTED` or `MF_E_NO_CAPTURE_DEVICES_AVAILABLE`.

### Device Detachment Procedure
To restore camera access to Windows:
```powershell
usbipd detach --busid <BUSID>
```
Upon detachment, Windows PnP immediately re-enumerates `USB\VID_0BDA&PID_58BA`, loads `usbvideo.sys`, and restores full functionality to WebcamLab.

---

## 2. Running the Investigation inside Linux / WSL2

Inside the Linux shell:
```bash
cd tools/linux
chmod +x run_probe.sh uvc_extension_query.py
./run_probe.sh
```

Or run Python directly:
```bash
python3 uvc_extension_query.py
```

Or compile and run the native C utility:
```bash
gcc -O2 -Wall uvc_xu_query.c -o uvc_xu_query
./uvc_xu_query /dev/video0
```

---

## 3. Strict Safety Protocol
- **Strict Read-Only**: The scripts execute `UVC_GET_LEN`, `UVC_GET_INFO`, `UVC_GET_CUR`, and `UVC_GET_DEF` only.
- **Zero Blind Mutations**: No `UVC_SET_CUR` or write requests are issued.
- **Hardware Protection**: Preserves EEPROM calibration structures and ISP runtime stability.
