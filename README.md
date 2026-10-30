# WebcamLab

**Native Windows Camera Recording & Sensor Diagnostic Application**  
*Laboratory-grade video capture, hardware ISP diagnostics, cinema look management, and multi-frame still enhancement for USB webcams.*

---

## Overview

WebcamLab is a native Windows desktop application designed to explore the upper physical limits of commodity USB webcams—specifically targeting the **Dell UZ2315H Integrated Multimedia Monitor Webcam** (`VID_0BDA&PID_58BA`, Realtek RTS5875 ISP).

Unlike standard camera applications that enforce generic OS auto-enhancements and non-linear video compression, WebcamLab provides:
- **Dual Recording Cadences:**
  - **Capture-Faithful VFR (Variable Frame Rate):** Preserves hardware camera timestamp delivery exactly as captured from the sensor, eliminating artificial duplicate frames.
  - **Broadcast CFR 30 FPS (Constant Frame Rate):** Paces video frames with deterministic duplicate-padding and millisecond-accurate audio/video synchronization for NLE timelines (Premiere, DaVinci Resolve, Final Cut).
- **Dual Recording Modes:**
  - **Original Camera:** Pristine, untouched bitstream recorded directly from hardware with zero color grading or filtering.
  - **Graded Presentation:** Non-destructive software cinema looks, 3D LUTs (17×17×17 or 33×33×33), tonal S-curves, and exposure compensation baked into an H.264 MP4 with companion JSON metadata sidecars.
- **Hardware ISP Control:**
  - Direct control of UVC VideoProcAmp parameters (Brightness, Contrast, Saturation, Sharpness, White Balance, Gain).
  - Reverse-engineered access to **Realtek Extension Unit 7 Control 2** (hardware edge-filtering modifier in 1080p YUY2 mode).
- **Multi-Frame Still Image Enhancement:**
  - Burst-capture fusion across 4 to 16 frames in uncompressed 1080p YUY2 or MJPEG.
  - Sub-pixel motion registration, sharpness-weighted reference frame selection, motion-masked temporal averaging, and high-frequency edge preservation yielding **+3.4 dB to +8.6 dB SNR improvement**.
- **Real-Time Telemetry & Scopes:**
  - Live FPS measurement, encoded frame counters, duplicate padding telemetry, audio sample rate, peak/RMS dBFS meters, clipping indicators, and 10-zone false-color exposure analysis.

---

## System Requirements

- **Operating System:** Windows 10 (Version 1903+) or Windows 11 (64-bit x64).
- **Architecture:** x64 (AMD64 / Intel 64).
- **Platform Dependencies:** Relies exclusively on standard Windows Media Foundation operating system components (`mfplat.dll`, `mfreadwrite.dll`, and Windows in-box H.264 and AAC Media Foundation Transforms). Zero external binaries or third-party executable runtimes required.
- **Runtime:** Fully self-contained .NET 10 x64 application bundle. No external .NET installation needed.
- **Camera Hardware:** Any UVC-compliant webcam (tested and calibrated specifically for the Dell UZ2315H monitor integrated camera).
- **Audio Hardware:** Any WASAPI-compatible microphone or headset.

---

## Quick Start

1. **Extract the Release Bundle:**
   Download and extract `WebcamLab-v1.1.0-rc5-native-win-x64.zip` to any folder on your computer.
2. **Launch Application:**
   Run `WebcamLab.exe`. No installer or administrative rights are required.
3. **Select Camera & Audio Device:**
   - Under the **Camera** dropdown, select `Dell Monitor Integrated Webcam` (or your webcam).
   - Under **Stream Format**, choose `1920 × 1080 @ 30 FPS (MJPG)` or `1920 × 1080 @ 5 FPS (YUY2)`.
   - Under **Audio Device**, select your microphone (e.g. `Microphone (Dell Monitor Integrated Webcam)`).
4. **Start Live Preview:**
   Click **▶ Start Stream**.
5. **Record Video:**
   - Select your desired **Recording Mode** (*Original Camera* or *Graded Presentation*).
   - Select your **Cadence Policy** (*Capture-Faithful VFR* or *CFR Compatibility 30fps*).
   - Click **● Record Video** (Shortcut: `Ctrl+R` or `Space`).
   - Click again to stop and finalize.
6. **Open Output Media:**
   Click **📁 Open Folder** or check `%USERPROFILE%\Videos\WebcamLab` for videos and `%USERPROFILE%\Pictures\WebcamLab` for photos.

---

## Project Documentation

- [**Installation & First-Run Guide**](INSTALL.md)
- [**Comprehensive Features & Workflows**](FEATURES.md)
- [**Troubleshooting & Diagnostic Guide**](TROUBLESHOOTING.md)
- [**Release Notes & Hardware Limits**](RELEASE_NOTES.md)
- [**Third-Party Licenses & Notices**](THIRD_PARTY_LICENSES.md)

---

## License

WebcamLab is licensed under the [MIT License](LICENSE).  
This native release candidate relies entirely on Microsoft Windows Media Foundation platform components and contains zero bundled GPL binaries. See [Third-Party Licenses](THIRD_PARTY_LICENSES.md) for full details.
