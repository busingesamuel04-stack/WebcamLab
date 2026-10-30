# Installation & First-Run Guide

This guide walks you through setting up and running WebcamLab on Windows.

---

## 1. Portable ZIP Installation (Recommended)

WebcamLab is distributed as a self-contained portable Windows application. It does not require administrative elevation, registry modifications, or external framework installations.

### Steps:
1. Download `WebcamLab-v1.1.0-rc5-native-win-x64.zip`.
2. Right-click the ZIP archive and select **Extract All...**.
3. Choose a destination folder (e.g., `C:\Tools\WebcamLab` or `D:\WebcamLab`).
   > [!NOTE]
   > Avoid extracting directly into protected Windows system directories (such as `C:\Program Files`) unless you intend to run with administrator permissions, as Windows restricts application write access in those locations.
4. Open the extracted folder and double-click `WebcamLab.exe` to launch.

---

## 2. Directory Structure of the Portable Native Release

```
WebcamLab-v1.1.0-rc5-native-win-x64/
├── WebcamLab.exe              # Main Application Executable (WPF, .NET 10 x64)
├── WebcamLab.dll              # Managed application core assembly
├── Assets/                    # Application icons and branding assets
│   ├── app.ico
│   └── app.png
├── Presets/                   # Factory calibration and LUT assets
│   └── LUTs/
│       ├── film_warmth.cube   # 3D Cinema LUT (Warm Amber)
│       ├── cool_steel.cube    # 3D Cinema LUT (Teal / Cool)
│       ├── black_and_white.cube # High-contrast monochrome LUT
│       └── identity.cube      # Linear pass-through LUT
├── README.md                  # Quick-start and overview
├── FEATURES.md                # Feature deep-dive and workflows
├── TROUBLESHOOTING.md         # Diagnostic and resolution guide
├── RELEASE_NOTES.md           # Version details and hardware limits
├── THIRD_PARTY_LICENSES.md    # Attribution notices (Zero GPL binaries)
└── LICENSE                    # Application MIT License
```

---

## 3. First-Run Setup & Device Permissions

### Camera & Microphone Access:
When running on Windows 10 or Windows 11 for the first time:
1. Ensure Windows Privacy Settings permit camera and microphone access:
   - Navigate to **Windows Settings > Privacy & Security > Camera**.
   - Verify that **Camera access** and **Let desktop apps access your camera** are toggled **ON**.
   - Navigate to **Windows Settings > Privacy & Security > Microphone**.
   - Verify that **Microphone access** and **Let desktop apps access your microphone** are toggled **ON**.
2. If using the **Dell UZ2315H Monitor**:
   - Ensure the **USB 3.0 upstream cable** is connected securely from the back of the monitor to a functional USB 3.0 port on your PC.
   - The monitor's integrated camera, stereo microphone array, and speakers communicate over this single upstream USB connection.

---

## 4. Default Storage Locations

WebcamLab automatically creates and organizes its output folders inside standard Windows user libraries:

| Media Type | Storage Directory | Formats |
| :--- | :--- | :--- |
| **Video Recordings** | `%USERPROFILE%\Videos\WebcamLab\` | `.mp4`, `.json` sidecars |
| **Still Snapshots** | `%USERPROFILE%\Pictures\WebcamLab\` | `.jpg` (original), `.png` (graded) |
| **Multi-Frame Bursts** | `%USERPROFILE%\Pictures\WebcamLab\` | `_Enhanced.png`, `_Original.png`, `.json` |
| **User Presets** | `%LOCALAPPDATA%\WebcamLab\Presets\` | `.json` custom camera presets |
| **User 3D LUTs** | `%LOCALAPPDATA%\WebcamLab\LUTs\` | `.cube` color lookup tables |
| **User Cinema Looks** | `%LOCALAPPDATA%\WebcamLab\Looks\` | `.json` custom look profiles |

You can access these folders at any time by clicking **📁 Open Videos Folder** or **📁 Open Stills Folder** on the **Settings** tab.

---

## 5. Uninstallation

Because WebcamLab is portable:
1. Delete the folder containing `WebcamLab.exe`.
2. (Optional) To remove saved user presets and LUTs, delete `%LOCALAPPDATA%\WebcamLab\`.
3. Captured videos and photos in `%USERPROFILE%\Videos\WebcamLab\` and `%USERPROFILE%\Pictures\WebcamLab\` are preserved and remain untouched.
