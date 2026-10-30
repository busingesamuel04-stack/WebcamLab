# Third-Party Licenses & Software Component Notices

**Product:** WebcamLab v1.1.0-rc5 Native Windows x64 (`WebcamLab-v1.1.0-rc5-native-win-x64`)  
**Publisher:** Antigravity Imaging  
**Distribution Model:** Self-contained native Windows desktop application with zero external GPL binaries.

---

## 1. Zero Bundled GPL / LGPL Binaries Declaration

This native release candidate **does not bundle, include, or distribute any FFmpeg executables or libraries** (`ffmpeg.exe`, `ffprobe.exe`, `avcodec`, `avformat`, `avutil`, etc.).

- **Video Encoding:** Microsoft Windows In-Box H.264 Video Encoder Media Foundation Transform (MFT). Utilizes hardware acceleration (Intel QuickSync, NVIDIA NVENC, AMD VCE) when supported by the graphics driver, falling back automatically to the Microsoft Software H.264 MFT.
- **Audio Encoding:** Microsoft Windows In-Box Advanced Audio Coding (AAC) MFT (`MFAudioFormat_AAC`).
- **Container Multiplexing:** Windows Media Foundation Sink Writer (`IMFSinkWriter` via `mfreadwrite.dll` / `mfplat.dll`).
- **Audio Capture:** Windows Audio Session API (`WASAPI` via `AudioClient`).
- **Licensing Impact:** Because all media encoding and multiplexing are performed exclusively by native Windows operating system components, this distribution bundle carries **no GNU General Public License (GPL) obligations** and does not require bundling or providing Corresponding Source for FFmpeg.

---

## 2. WebcamLab Application License

WebcamLab application source code, custom algorithms, and factory look profiles are licensed under the MIT License:

```
MIT License

Copyright (c) 2026 Antigravity Imaging

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 3. Microsoft .NET Runtime & Windows Presentation Foundation (WPF)

- **Components:** .NET 10 CoreCLR runtime (`coreclr.dll`, `clrjit.dll`, `clrgc.dll`), Base Class Library assemblies (`System.*`, `Microsoft.*`), and WPF composition binaries (`wpfgfx_cor3.dll`).
- **Copyright:** Copyright (c) .NET Foundation and Contributors. All rights reserved.
- **License:** MIT License

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors
All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 4. Microsoft Windows Desktop & DirectX SDK Redistributables

The application bundle includes select native binary redistributables provided by Microsoft Corporation to support Windows desktop composition and DirectX hardware rendering:

- **Components:**
  - `vcruntime140_cor3.dll` (Microsoft Visual C++ Runtime component)
  - `D3DCompiler_47_cor3.dll` (Direct3D Shader Compiler)
  - `DirectWriteForwarder.dll` (DirectWrite text layout integration)
- **Copyright:** Copyright (c) Microsoft Corporation. All rights reserved.
- **Licensing Terms:** Distributed under the **Microsoft Software License Terms for Microsoft .NET and Windows SDK Redistributable Code**. These binaries are licensed for redistribution with applications built using the Microsoft Windows Desktop SDK and .NET.

---

## 5. In-Box Windows Platform Dependencies (Non-Bundled)

The application dynamically references the following shared operating system libraries located in `%SystemRoot%\System32`:
- `mfplat.dll` / `mfreadwrite.dll` / `mf.dll` (Microsoft Windows Media Foundation)
- `d3d11.dll` / `dxgi.dll` (Microsoft DirectX 11 / DXGI Runtime)
- `user32.dll` / `gdi32.dll` / `ole32.dll` (Microsoft Windows Core Win32 APIs)
- `msvcrt.dll` / `ucrtbase.dll` (Microsoft Universal C Runtime)

These platform components are covered by the Microsoft Windows Operating System License and are pre-installed on supported Windows 10 and Windows 11 systems.
