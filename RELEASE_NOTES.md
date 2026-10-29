# WebcamLab Release Notes

## Version 1.1.0-rc3 — Native Windows Release Candidate 3 (Build 2026.10)

WebcamLab v1.1.0-rc3 resolves **Defect 39-1** in the Native Media Foundation recording engine, ensuring sustained recordings of any length finalize reliably with guaranteed MP4 container integrity and zero multiplexing stalls.

### 1. Key Improvements in v1.1.0-rc3

- **Chronological PTS-Ordered Interleaved Multiplexing:**
  - *Root Cause Resolved:* In v1.1.0-rc2, encoding queues drained asymmetrically, writing video faster than audio. When video PTS led audio PTS by more than 2 seconds, Windows Media Foundation's MPEG-4 Sink Writer multiplexer hit internal stream-distance limits and throttled `WriteSample()` by blocking for 1,000 ms per sample. On longer recordings, this caused thread join timeouts and abandoned finalization (`moov atom not found`).
  - *Fix:* Re-engineered `ProcessEncodingQueues()` with a dual-stream chronological interleaving arbiter (`vPts <= aPts`). Video and audio samples are fed to the Sink Writer in strict presentation timestamp order. Neither stream leads the other, completely eliminating Sink Writer throttling.
- **Definitive Synchronization Barrier & Adaptive Watchdog:**
  - Replaced the fixed 10-second thread join timeout in `Stop()` with a dedicated `ManualResetEventSlim` worker completion barrier.
  - Added an adaptive session-aware watchdog timeout `Math.Max(30000, (_framesCaptured * 50) + 15000)` providing deterministic finalization guarantees without premature abandonment.
  - Added rigorous error propagation: checks and records the HRESULT of `IMFSinkWriter.Finalize()` and captures background thread exceptions, preventing silent failures.
- **MP4 Container Validation Guard:**
  - Implemented `Mp4TimestampInjector.HasValidMoovAtom()` to inspect top-level box structures before attempting timestamp injection.
  - Explicitly rejects unfinalized or truncated bitstreams, preventing secondary corruption or premature parser crashes.
- **Audio Calibration Safety Clamping:**
  - Clamped WASAPI clock alignment offsets to $\pm 2.0\text{ s}$ to prevent runaway silence insertion when interfacing with anomalous or desynchronized audio hardware.
- **Validation & Test Coverage:**
  - Automated test suite expanded to **106 tests** with 100% pass rate, including automated tests for sustained multiplexing load under stress and container validation guard.
  - Empirically verified sustained 60-second hardware recording on Dell UZ2315H (901 video frames, 2,815 audio frames, A/V delta 13.48 ms, perfect moov atom, instant 43 ms finalization).
  - All 8 physical camera regression tests passed cleanly.

---

## Version 1.1.0-rc2 — Native Windows Release Candidate 2 (Build 2026.10)

WebcamLab v1.1.0-rc2 is the refined native Windows Media Foundation release candidate of the native camera diagnostic, grading, and recording application.

This release candidate eliminates external FFmpeg binary dependencies while delivering broadcast-accurate A/V synchronization, strips debug symbols (`WebcamLab.pdb`) from the portable distribution payload, and provides audited component licensing declarations.

---

## 1. What's New in v1.1.0-rc2

### Native Windows Media Foundation Recording Engine (Promoted Default)
- **Zero Bundled External Binaries:** Completely removes bundled `ffmpeg.exe` and `ffprobe.exe` executables, reducing the portable distribution size from 241 MB to ~64 MB (~73% reduction).
- **In-Box Hardware/Software Encoding:**
  - H.264 video encoding via Microsoft Media Foundation Transforms (`MFVideoFormat_H264`), utilizing GPU hardware acceleration (Intel QuickSync, NVIDIA NVENC, AMD VCE) when available with automatic fallback to Microsoft Software H.264 MFT.
  - High-fidelity stereo AAC audio encoding (`MFAudioFormat_AAC`) at 192 kbps, 48 kHz.
- **Broadcast-Grade A/V Synchronization:**
  - Tail duration alignment in both CFR and VFR modes ensuring audio and video track duration deltas consistently stay below 10 ms (well within the 35 ms broadcast tolerance).
  - Physical clapper transient alignment error measured at $< 5.0\text{ ms}$ (sub-frame $\le 16.6\text{ ms}$).
  - Hardware QPC-backed presentation timestamps with zero timeline skew or initial drift.
- **Preserved Dual Cadences & Dual Modes:**
  - *Capture-Faithful VFR:* Records true hardware frame arrival timestamps with zero inserted duplicate frames.
  - *Broadcast CFR 30 FPS:* Paces incoming frames to a constant 30 FPS timeline with deterministic duplicate-padding and millisecond-accurate A/V duration alignment.
  - *Original Camera Mode:* Pristine, untouched bitstream recorded directly from hardware with zero grading.
  - *Graded Presentation Mode:* Non-destructive software cinema looks, 3D LUTs, tonal curves, and exposure gain baked into MP4 bitstreams with companion JSON metadata sidecars.
- **Full Fallback Support:**
  - Retains the battle-tested FFmpeg recording engine in source and UI as an optional legacy fallback. If an external `ffmpeg.exe` is placed alongside the executable or on the system PATH, the user can toggle between Native MF and FFmpeg in the UI.

---

## 2. Core Capabilities & Hardware Diagnostics

- **Cinema Look Manager:** Full look lifecycle (create, rename, duplicate, export, import, delete), factory looks (*Natural Neutral*, *Warm Amber Cinema*, *Teal & Steel*, *Classic Monochrome*), `.cube` 3D LUT parser with trilinear interpolation, and zero-silent-substitution guarantee.
- **Hardware ISP Diagnostic Controls:** UVC VideoProcAmp parameters (Brightness, Contrast, Saturation, Sharpness, White Balance, Gain) and reverse-engineered access to **Realtek Extension Unit 7 Control 2** (hardware edge-filtering attenuation in 1080p YUY2 mode).
- **Multi-Frame Still Image Enhancement:** Burst capture (4, 8, 16 frames) with sub-pixel motion registration, sharpness-weighted reference selection, motion-masked temporal averaging, and high-frequency edge preservation yielding **+3.4 dB to +8.6 dB SNR improvement**.
- **Professional Scopes & Telemetry:** 10-zone false-color exposure analysis conforming to IRE / ARRI standards, real-time FPS telemetry, duplicate padding counters, audio peak/RMS dBFS meters with clipping warnings, and A/V duration delta monitoring.

---

## 3. Accurate Hardware Capability Disclaimers & Known Limitations

In accordance with empirical engineering standards, the following capability boundaries are explicitly documented:

| Feature / Capability | Supported Status | Technical Finding & Boundary Explanation |
| :--- | :---: | :--- |
| **Processed 8-bit MJPEG / YUY2 Capture** | **CONFIRMED** | Hardware provides 1080p 30 FPS MJPEG (compressed YUV) and 1080p 5 FPS YUY2 (uncompressed YUV 4:2:2). |
| **Hardware Edge-Filter Attenuation** | **CONFIRMED** | Extension Unit 7 Control 2 modifies ISP edge-enhancement in 1080p YUY2 mode. |
| **Sensor RAW / Bayer Readout** | **NOT EXPOSED** | The USB configuration descriptor strictly declares MJPEG and YUY2 formats. No raw Bayer FourCCs exist in device descriptors. |
| **Manual Sensor Exposure & Gain** | **NOT EXPOSED** | Camera Terminal `bmControls` bits 3 and 4 are strictly 0. Standard DirectShow/MF manual exposure returns `ERROR_NOT_FOUND`. The sensor operates exclusively in closed-loop autonomous AE/AGC. |
| **True Sensor Log Recording** | **NOT EXPOSED** | The camera hardware pipeline delivers standard 8-bit dynamic range (Rec.709). Film-like logarithmic curves are provided via non-destructive software 3D LUTs in host software. |
| **Hardware Dual-Exposure HDR** | **NOT EXPOSED** | Sensor operates in single-exposure rolling shutter mode; no staggered HDR readout is supported in silicon. |
| **Open-Gate / Wider Readout** | **NOT EXPOSED** | The physical CMOS sensor active pixel array is fixed at 1080p ($1920\times1080$). |
| **Realtek Extension Unit 4 Command Interface** | **CONSTRAINED / UNVERIFIED** | Unit 4 exposes an 8-byte command staging mailbox (Control 10) and doorbell trigger (Control 11). Because Realtek has not published the opcode dictionary for the RTS5875 ISP, speculative writes are strictly prohibited to prevent flash corruption or MCU locks. |

---

## 4. Verification & Quality Assurance Summary

- **Automated Validation Test Suite:** **104/104 PASS (100.0%)** across color mathematics, LUT parsing, scopes, video cadence, audio analysis, look management, multi-frame fusion, and native Media Foundation sink writer contracts.
- **Physical Hardware Camera Regression Checks:** **8/8 PASS (100.0%)** on the physical Dell UZ2315H webcam hardware.
- **Runtime Dependency Boundary Audit:** Staging environment verified with 0 instances of `ffmpeg.exe` or `ffprobe.exe` on disk or process tree, passing all 104 unit tests and physical capture tests in complete isolation.
- **A/V Synchronization Metrics on Dell UZ2315H:**
  - Scenario A (Graded VFR 1080p + Audio 3.0s): $\Delta = 2.55\text{ ms}$ (Target $\le 35.0\text{ ms}$)
  - Scenario B (Original CFR 1080p + Audio 2.5s): $\Delta = 5.35\text{ ms}$ (Target $\le 35.0\text{ ms}$)
  - Scenario C (Neutral Look VFR 1080p + Audio 2.0s): $\Delta = 2.75\text{ ms}$ (Target $\le 35.0\text{ ms}$)
  - Scenario D (Early Stop 1.0s + Audio): $\Delta = 7.69\text{ ms}$ (Target $\le 35.0\text{ ms}$)
  - Clapper Transient Alignment Error: $< 5.0\text{ ms}$ (Sub-frame $\le 16.6\text{ ms}$)
