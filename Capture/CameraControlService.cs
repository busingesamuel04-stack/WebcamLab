using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WebcamLab.Core.Models;
using WebcamLab.Core.Native;

namespace WebcamLab.Capture;

public class CameraControlService
{
    private readonly object _lock = new();
    private MediaFoundationNative.IMFMediaSource? _activeMediaSource;
    private IAMVideoProcAmp? _activeVideoProcAmp;
    private IAMCameraControl? _activeCameraControl;

    public void AttachLiveMediaSource(MediaFoundationNative.IMFMediaSource mediaSource)
    {
        lock (_lock)
        {
            _activeMediaSource = mediaSource;
            _activeVideoProcAmp = mediaSource as IAMVideoProcAmp;
            _activeCameraControl = mediaSource as IAMCameraControl;
        }
    }

    public void DetachLiveMediaSource()
    {
        lock (_lock)
        {
            _activeMediaSource = null;
            _activeVideoProcAmp = null;
            _activeCameraControl = null;
        }
    }

    public Task<IReadOnlyList<CameraHardwareProperty>> QueryPropertiesAsync(CameraDevice device)
    {
        return Task.Run<IReadOnlyList<CameraHardwareProperty>>(() =>
        {
            var properties = new List<CameraHardwareProperty>();

            lock (_lock)
            {
                // If we already have an active media source for this device, query it directly
                if (_activeMediaSource != null)
                {
                    QueryFromMediaSource(_activeMediaSource, properties);
                    return properties;
                }
            }

            // Otherwise activate device temporarily to query descriptor controls
            int hr = MediaFoundationNative.MFCreateAttributes(out var attr, 1);
            if (hr != 0) return properties;

            var sourceType = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
            var vidCap = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
            attr.SetGUID(ref sourceType, ref vidCap);

            hr = MediaFoundationNative.MFEnumDeviceSources(attr, out IntPtr ppDevices, out uint count);
            if (hr != 0 || device.Index >= count) return properties;

            IntPtr[] activates = new IntPtr[count];
            Marshal.Copy(ppDevices, activates, 0, (int)count);

            var activate = (MediaFoundationNative.IMFActivate)Marshal.GetObjectForIUnknown(activates[device.Index]);
            var iidSource = typeof(MediaFoundationNative.IMFMediaSource).GUID;
            hr = activate.ActivateObject(ref iidSource, out object sourceObj);
            if (hr == 0)
            {
                var mediaSource = (MediaFoundationNative.IMFMediaSource)sourceObj;
                QueryFromMediaSource(mediaSource, properties);

                Marshal.ReleaseComObject(mediaSource);
                activate.ShutdownObject();
            }

            Marshal.ReleaseComObject(activate);
            for (uint i = 0; i < count; i++)
            {
                if (i != device.Index) Marshal.Release(activates[i]);
            }
            MediaFoundationNative.CoTaskMemFree(ppDevices);

            return properties;
        });
    }

    private static void QueryFromMediaSource(MediaFoundationNative.IMFMediaSource mediaSource, List<CameraHardwareProperty> properties)
    {
        var procAmp = mediaSource as IAMVideoProcAmp;
        var camCtrl = mediaSource as IAMCameraControl;

        // 1. Query VideoProcAmp properties
        foreach (VideoProcAmpProperty prop in Enum.GetValues<VideoProcAmpProperty>())
        {
            string name = prop switch
            {
                VideoProcAmpProperty.Brightness => "Brightness",
                VideoProcAmpProperty.Contrast => "Contrast",
                VideoProcAmpProperty.Hue => "Hue",
                VideoProcAmpProperty.Saturation => "Saturation",
                VideoProcAmpProperty.Sharpness => "Sharpness",
                VideoProcAmpProperty.Gamma => "Gamma",
                VideoProcAmpProperty.ColorEnable => "Color Enable",
                VideoProcAmpProperty.WhiteBalance => "White Balance (Kelvin)",
                VideoProcAmpProperty.BacklightCompensation => "Backlight Compensation",
                VideoProcAmpProperty.Gain => "Gain",
                _ => prop.ToString()
            };

            string desc = prop switch
            {
                VideoProcAmpProperty.Brightness => "Black level offset adjustment",
                VideoProcAmpProperty.Contrast => "Luminance transfer slope/spread",
                VideoProcAmpProperty.Hue => "Chrominance phase rotation",
                VideoProcAmpProperty.Saturation => "Color purity/amplitude",
                VideoProcAmpProperty.Sharpness => "High-frequency edge enhancement filter",
                VideoProcAmpProperty.Gamma => "Non-linear tone curve encoding",
                VideoProcAmpProperty.ColorEnable => "Color subcarrier modulation toggle",
                VideoProcAmpProperty.WhiteBalance => "Correlated color temperature adjustment",
                VideoProcAmpProperty.BacklightCompensation => "Shadow boost for high dynamic range backlighting",
                VideoProcAmpProperty.Gain => "Sensor analog/digital signal amplification",
                _ => string.Empty
            };

            if (procAmp != null)
            {
                int hr = procAmp.GetRange(prop, out int min, out int max, out int step, out int def, out VideoProcAmpFlags caps);
                if (hr == 0)
                {
                    int ghr = procAmp.Get(prop, out int cur, out VideoProcAmpFlags curFlags);
                    bool supportsAuto = (caps & VideoProcAmpFlags.Auto) != 0;
                    bool supportsManual = (caps & VideoProcAmpFlags.Manual) != 0;
                    bool isAuto = (curFlags & VideoProcAmpFlags.Auto) != 0;

                    int initialVal = ghr == 0 ? cur : def;
                    bool initialAuto = isAuto;

                    properties.Add(new CameraHardwareProperty
                    {
                        Name = name,
                        Description = desc,
                        PropertyType = HardwarePropertyType.VideoProcAmp,
                        PropertyId = (int)prop,
                        IsSupported = true,
                        Min = min,
                        Max = max,
                        Step = step > 0 ? step : 1,
                        DefaultValue = def,
                        InitialValue = initialVal,
                        InitialIsAuto = initialAuto,
                        CurrentValue = initialVal,
                        SupportsAuto = supportsAuto,
                        SupportsManual = supportsManual,
                        IsAuto = initialAuto,
                        DiagnosticDetails = $"Supported (Range: [{min}..{max}], Step: {step}, Caps: {caps})"
                    });
                    continue;
                }
                else
                {
                    properties.Add(new CameraHardwareProperty
                    {
                        Name = name,
                        Description = desc,
                        PropertyType = HardwarePropertyType.VideoProcAmp,
                        PropertyId = (int)prop,
                        IsSupported = false,
                        DiagnosticDetails = $"Unsupported (hr=0x{hr:X8} / ERROR_NOT_FOUND)"
                    });
                }
            }
            else
            {
                properties.Add(new CameraHardwareProperty
                {
                    Name = name,
                    Description = desc,
                    PropertyType = HardwarePropertyType.VideoProcAmp,
                    PropertyId = (int)prop,
                    IsSupported = false,
                    DiagnosticDetails = "IAMVideoProcAmp interface not implemented by source"
                });
            }
        }

        // 2. Query CameraControl properties
        foreach (CameraControlProperty prop in Enum.GetValues<CameraControlProperty>())
        {
            string name = prop switch
            {
                CameraControlProperty.Pan => "Pan",
                CameraControlProperty.Tilt => "Tilt",
                CameraControlProperty.Roll => "Roll",
                CameraControlProperty.Zoom => "Zoom",
                CameraControlProperty.Exposure => "Exposure (Shutter)",
                CameraControlProperty.Iris => "Iris (Aperture)",
                CameraControlProperty.Focus => "Focus",
                _ => prop.ToString()
            };

            string desc = prop switch
            {
                CameraControlProperty.Pan => "Horizontal sensor/mount rotation",
                CameraControlProperty.Tilt => "Vertical sensor/mount rotation",
                CameraControlProperty.Roll => "Z-axis optical roll",
                CameraControlProperty.Zoom => "Optical or digital focal length magnification",
                CameraControlProperty.Exposure => "Integration duration / sensor shutter timing",
                CameraControlProperty.Iris => "Physical diaphragm f-stop aperture",
                CameraControlProperty.Focus => "Lens focal plane displacement distance",
                _ => string.Empty
            };

            if (camCtrl != null)
            {
                int hr = camCtrl.GetRange(prop, out int min, out int max, out int step, out int def, out CameraControlFlags caps);
                if (hr == 0)
                {
                    int ghr = camCtrl.Get(prop, out int cur, out CameraControlFlags curFlags);
                    bool supportsAuto = (caps & CameraControlFlags.Auto) != 0;
                    bool supportsManual = (caps & CameraControlFlags.Manual) != 0;
                    bool isAuto = (curFlags & CameraControlFlags.Auto) != 0;

                    int initialVal = ghr == 0 ? cur : def;
                    bool initialAuto = isAuto;

                    properties.Add(new CameraHardwareProperty
                    {
                        Name = name,
                        Description = desc,
                        PropertyType = HardwarePropertyType.CameraControl,
                        PropertyId = (int)prop,
                        IsSupported = true,
                        Min = min,
                        Max = max,
                        Step = step > 0 ? step : 1,
                        DefaultValue = def,
                        InitialValue = initialVal,
                        InitialIsAuto = initialAuto,
                        CurrentValue = initialVal,
                        SupportsAuto = supportsAuto,
                        SupportsManual = supportsManual,
                        IsAuto = initialAuto,
                        DiagnosticDetails = $"Supported (Range: [{min}..{max}], Step: {step})"
                    });
                }
                else
                {
                    properties.Add(new CameraHardwareProperty
                    {
                        Name = name,
                        Description = desc,
                        PropertyType = HardwarePropertyType.CameraControl,
                        PropertyId = (int)prop,
                        IsSupported = false,
                        DiagnosticDetails = $"Unsupported (hr=0x{hr:X8} / ERROR_NOT_FOUND - camera ISP handles internally)"
                    });
                }
            }
            else
            {
                properties.Add(new CameraHardwareProperty
                {
                    Name = name,
                    Description = desc,
                    PropertyType = HardwarePropertyType.CameraControl,
                    PropertyId = (int)prop,
                    IsSupported = false,
                    DiagnosticDetails = "IAMCameraControl interface not implemented by source"
                });
            }
        }
    }

    public bool SetProperty(CameraHardwareProperty prop, int value, bool isAuto)
    {
        if (!prop.IsSupported) return false;

        lock (_lock)
        {
            if (prop.PropertyType == HardwarePropertyType.VideoProcAmp)
            {
                if (_activeVideoProcAmp == null)
                {
                    prop.StatusMessage = "No active capture stream to apply setting.";
                    return false;
                }

                var p = (VideoProcAmpProperty)prop.PropertyId;
                var flags = isAuto ? VideoProcAmpFlags.Auto : VideoProcAmpFlags.Manual;

                // Clamp value within hardware limits
                int clamped = Math.Clamp(value, prop.Min, prop.Max);

                int hr = _activeVideoProcAmp.Set(p, clamped, flags);
                if (hr == 0)
                {
                    // Read back to confirm
                    int ghr = _activeVideoProcAmp.Get(p, out int readback, out VideoProcAmpFlags curFlags);
                    if (ghr == 0)
                    {
                        prop.CurrentValue = readback;
                        prop.IsAuto = (curFlags & VideoProcAmpFlags.Auto) != 0;
                        prop.StatusMessage = $"Hardware confirmed: {readback} ({curFlags})";
                        return true;
                    }
                    prop.CurrentValue = clamped;
                    prop.IsAuto = isAuto;
                    prop.StatusMessage = "Setting applied (read-back pending)";
                    return true;
                }

                prop.StatusMessage = $"Set failed: hr=0x{hr:X8}";
                return false;
            }
            else if (prop.PropertyType == HardwarePropertyType.CameraControl)
            {
                if (_activeCameraControl == null)
                {
                    prop.StatusMessage = "No active capture stream to apply setting.";
                    return false;
                }

                var p = (CameraControlProperty)prop.PropertyId;
                var flags = isAuto ? CameraControlFlags.Auto : CameraControlFlags.Manual;
                int clamped = Math.Clamp(value, prop.Min, prop.Max);

                int hr = _activeCameraControl.Set(p, clamped, flags);
                if (hr == 0)
                {
                    int ghr = _activeCameraControl.Get(p, out int readback, out CameraControlFlags curFlags);
                    if (ghr == 0)
                    {
                        prop.CurrentValue = readback;
                        prop.IsAuto = (curFlags & CameraControlFlags.Auto) != 0;
                        prop.StatusMessage = $"Hardware confirmed: {readback}";
                        return true;
                    }
                    prop.CurrentValue = clamped;
                    prop.IsAuto = isAuto;
                    return true;
                }

                prop.StatusMessage = $"Set failed: hr=0x{hr:X8}";
                return false;
            }

            return false;
        }
    }

    public bool ResetProperty(CameraHardwareProperty prop)
    {
        if (!prop.IsSupported) return false;
        bool defaultIsAuto = prop.SupportsAuto && (prop.PropertyId == (int)VideoProcAmpProperty.WhiteBalance);
        return SetProperty(prop, prop.DefaultValue, defaultIsAuto);
    }

    public bool RestoreInitialProperty(CameraHardwareProperty prop)
    {
        if (!prop.IsSupported) return false;
        return SetProperty(prop, prop.InitialValue, prop.InitialIsAuto);
    }

    /// <summary>
    /// Reads and returns live hardware control values from the attached camera device.
    /// </summary>
    public IReadOnlyList<PresetControlValue> GetCurrentControlValues()
    {
        var list = new List<PresetControlValue>();
        lock (_lock)
        {
            if (_activeVideoProcAmp != null)
            {
                foreach (VideoProcAmpProperty prop in Enum.GetValues<VideoProcAmpProperty>())
                {
                    int hr = _activeVideoProcAmp.Get(prop, out int val, out VideoProcAmpFlags flags);
                    if (hr == 0)
                    {
                        list.Add(new PresetControlValue
                        {
                            PropertyName = prop.ToString(),
                            PropertyType = HardwarePropertyType.VideoProcAmp,
                            PropertyId = (int)prop,
                            Value = val,
                            IsAuto = (flags & VideoProcAmpFlags.Auto) != 0
                        });
                    }
                }
            }

            if (_activeCameraControl != null)
            {
                foreach (CameraControlProperty prop in Enum.GetValues<CameraControlProperty>())
                {
                    int hr = _activeCameraControl.Get(prop, out int val, out CameraControlFlags flags);
                    if (hr == 0)
                    {
                        list.Add(new PresetControlValue
                        {
                            PropertyName = prop.ToString(),
                            PropertyType = HardwarePropertyType.CameraControl,
                            PropertyId = (int)prop,
                            Value = val,
                            IsAuto = (flags & CameraControlFlags.Auto) != 0
                        });
                    }
                }
            }
        }
        return list;
    }
}
