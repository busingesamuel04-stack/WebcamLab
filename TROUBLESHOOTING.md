# WebcamLab Troubleshooting & Diagnostics

This guide provides practical resolutions for common hardware, driver, and system configuration issues.

---

## 1. Camera Discovery & Connection Issues

### Symptom: "No Video Capture Devices Found"
- **Cause:** The webcam is either disconnected, blocked by Windows Privacy settings, or locked by another application.
- **Resolution:**
  1. **Check Physical Connection:** If using the Dell UZ2315H, ensure the USB 3.0 upstream cable is firmly plugged into both the monitor and PC.
  2. **Verify Windows Privacy Settings:**
     - Open Windows **Settings > Privacy & Security > Camera**.
     - Ensure **Let desktop apps access your camera** is enabled.
  3. **Check for Exclusivity Locks:** Close other applications that may have exclusive handles on the camera (Teams, Zoom, Skype, OBS, Windows Camera).
  4. **Refresh Device List:** Click **Refresh Devices** in WebcamLab.

---

## 2. Stream Format & Frame Rate Issues

### Symptom: Low Frame Rate in MJPEG Mode (e.g. 5–15 FPS instead of 30 FPS)
- **Cause:** CMOS sensors on USB webcams automatically lower frame rates under dim lighting conditions to increase exposure time per frame (auto-exposure priority).
- **Resolution:**
  1. Increase ambient lighting on your face and background.
  2. Under the **Camera** tab, adjust **Brightness** or disable auto-exposure priority if supported.
  3. Note that in **Capture-Faithful VFR** mode, WebcamLab records the exact rate delivered by the sensor, whereas in **CFR 30fps** mode, it pads dropped frames with duplicate slots to maintain a constant 30 FPS timeline.

### Symptom: YUY2 Mode is Capped at 5 FPS
- **Explanation:** This is a physical USB 2.0 bandwidth constraint on the Dell UZ2315H. 1080p uncompressed YUY2 ($1920 \times 1080 \times 16\text{ bpp} \times 5\text{ fps} \approx 166\text{ Mbps}$) approaches the effective isochronous bandwidth limit of the controller. 5 FPS is the camera's maximum hardware specification for uncompressed 1080p.

---

## 3. Audio & A/V Synchronization Issues

### Symptom: Audio Recording Failed / Silence in Output
- **Cause:** The selected WASAPI audio endpoint is muted, disabled, or in use by another application in exclusive mode.
- **Resolution:**
  1. Open Windows **Sound Settings > Input**.
  2. Ensure the microphone input volume is above 0% and test that the level meter moves.
  3. In WebcamLab, select the correct input under the **Audio Device** dropdown.
  4. Ensure **Record Audio with Video** is checked.
  5. Check the live audio meter in the Telemetry drawer to verify that the peak meter responds to speech.

### Symptom: Noticeable Audio/Video Desynchronization in Video Files
- **Resolution:**
  - If editing in video editors (Premiere, DaVinci Resolve), ensure you recorded in **CFR Compatibility (30 FPS)** mode. NLE timelines often experience A/V drift when handling VFR media.
  - WebcamLab's CFR engine aligns video and audio tracks with sub-25ms duration parity.

---

## 4. FFmpeg Encoding & Finalization Issues

### Symptom: "FFmpeg executable not found"
- **Cause:** The portable bundle's `ffmpeg.exe` was moved or deleted, or system PATH is missing FFmpeg.
- **Resolution:**
  1. Verify that `ffmpeg.exe` is present in the same folder as `WebcamLab.exe` or inside `tools\ffmpeg\`.
  2. Alternatively, install FFmpeg system-wide via WinGet:
     ```powershell
     winget install Gyan.FFmpeg
     ```
  3. Restart WebcamLab. The application automatically searches the application folder, `tools\ffmpeg\`, system PATH, and the WinGet package repository.

### Symptom: Recording Takes Long to Finalize After Clicking Stop
- **Explanation:** When stopping a long or high-bitrate recording, FFmpeg flushes all encoded video frames, writes the MP4 container `moov` atom, and interleaves audio packets. Allow up to 10–30 seconds for complete finalization on slower drives. Do not force-terminate the application while finalization is in progress.

---

## 5. 3D LUT & Cinema Look Issues

### Symptom: "LUT file 'xxx.cube' not found on disk"
- **Explanation:** A preset references an external 3D LUT that has been moved or renamed.
- **WebcamLab Safeguard:** The application **does not silently substitute** another LUT. It bypasses the LUT transformation while leaving your exposure, contrast, saturation, and tonal curves fully active.
- **Resolution:**
  1. Copy your custom `.cube` file into `%LOCALAPPDATA%\WebcamLab\LUTs\`.
  2. Or click **Load LUT...** on the **Looks** tab and browse to the `.cube` file.

---

## 6. Built-in Diagnostic CLI Flags

WebcamLab contains isolated command-line diagnostic suites for hardware inspection and automated regression testing. You can run these from PowerShell or Command Prompt:

| Command | Purpose |
| :--- | :--- |
| `WebcamLab.exe --test` | Runs the full 83-point automated mathematical and software look test suite. |
| `WebcamLab.exe --regression` | Runs the 8-point physical hardware camera and audio regression suite. |
| `WebcamLab.exe --phase24` | Interrogates Realtek Extension Unit 4 in read-only mode and dumps complete register telemetry. |
| `WebcamLab.exe --formats` | Enumerates all 50 physical stream formats supported by the connected camera. |
