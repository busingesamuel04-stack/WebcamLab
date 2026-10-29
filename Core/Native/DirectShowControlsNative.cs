using System;
using System.Runtime.InteropServices;

namespace WebcamLab.Core.Native;

[Flags]
public enum CameraControlFlags
{
    None = 0,
    Auto = 0x0001,
    Manual = 0x0002
}

public enum CameraControlProperty
{
    Pan = 0,
    Tilt = 1,
    Roll = 2,
    Zoom = 3,
    Exposure = 4,
    Iris = 5,
    Focus = 6
}

[ComImport]
[Guid("C6E13370-30AC-11D0-A18C-00A0C9118956")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAMCameraControl
{
    [PreserveSig]
    int GetRange(CameraControlProperty Property, out int pMin, out int pMax, out int pSteppingDelta, out int pDefault, out CameraControlFlags pCapsFlags);

    [PreserveSig]
    int Set(CameraControlProperty Property, int lValue, CameraControlFlags Flags);

    [PreserveSig]
    int Get(CameraControlProperty Property, out int lValue, out CameraControlFlags Flags);
}

[Flags]
public enum VideoProcAmpFlags
{
    None = 0,
    Auto = 0x0001,
    Manual = 0x0002
}

public enum VideoProcAmpProperty
{
    Brightness = 0,
    Contrast = 1,
    Hue = 2,
    Saturation = 3,
    Sharpness = 4,
    Gamma = 5,
    ColorEnable = 6,
    WhiteBalance = 7,
    BacklightCompensation = 8,
    Gain = 9
}

[ComImport]
[Guid("C6E13360-30AC-11D0-A18C-00A0C9118956")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAMVideoProcAmp
{
    [PreserveSig]
    int GetRange(VideoProcAmpProperty Property, out int pMin, out int pMax, out int pSteppingDelta, out int pDefault, out VideoProcAmpFlags pCapsFlags);

    [PreserveSig]
    int Set(VideoProcAmpProperty Property, int lValue, VideoProcAmpFlags Flags);

    [PreserveSig]
    int Get(VideoProcAmpProperty Property, out int lValue, out VideoProcAmpFlags Flags);
}
