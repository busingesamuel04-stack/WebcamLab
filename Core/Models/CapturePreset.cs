using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebcamLab.Core.Models;

/// <summary>
/// Target setting for a single hardware picture control within a capture preset.
/// </summary>
public class PresetControlValue
{
    public string PropertyName { get; set; } = string.Empty;
    public HardwarePropertyType PropertyType { get; set; }
    public int PropertyId { get; set; }
    public int Value { get; set; }
    public bool IsAuto { get; set; }

    public PresetControlValue() { }

    public PresetControlValue(CameraHardwareProperty prop)
    {
        PropertyName = prop.Name;
        PropertyType = prop.PropertyType;
        PropertyId = prop.PropertyId;
        Value = prop.CurrentValue;
        IsAuto = prop.IsAuto;
    }
}

/// <summary>
/// Vendor-specific hardware ISP settings (Phase 15 extension point).
/// Isolated from standard software look parameters and applied strictly
/// to verified vendor hardware (Realtek RTS5875).
/// </summary>
public class VendorHardwareSettings
{
    public string TargetVendor { get; set; } = "Realtek";
    public string TargetDeviceVidPid { get; set; } = "VID_0BDA&PID_58BA";
    public bool ApplyExperimentalProcessing { get; set; } = false;
    public byte RealtekUnit7Control2Value { get; set; } = 0x01; // 0x01 = Standard, 0x00 = Reduced Detail

    public VendorHardwareSettings Clone() => new()
    {
        TargetVendor = TargetVendor,
        TargetDeviceVidPid = TargetDeviceVidPid,
        ApplyExperimentalProcessing = ApplyExperimentalProcessing,
        RealtekUnit7Control2Value = RealtekUnit7Control2Value
    };
}

/// <summary>
/// Thread-safe settings model for Phase 7 Software Look Pipeline.
/// Controls non-destructive post-processing on the presentation layer.
/// </summary>
public class SoftwareLookSettings
{
    public bool Enabled { get; set; } = true;
    public string LookName { get; set; } = "Neutral (Bypass)";
    public string ToneCurveName { get; set; } = "Linear (Passthrough)";

    /// <summary>
    /// Software exposure offset in EV (-2.0 to +2.0). 0.0 is neutral.
    /// Labeled strictly as software digital gain, not sensor shutter duration.
    /// </summary>
    public double ExposureOffset { get; set; } = 0.0;

    /// <summary>
    /// Software contrast multiplier (0.5 to 2.0). 1.0 is neutral.
    /// </summary>
    public double Contrast { get; set; } = 1.0;

    /// <summary>
    /// Software saturation multiplier (0.0 to 2.0). 1.0 is neutral; 0.0 is monochrome.
    /// </summary>
    public double Saturation { get; set; } = 1.0;

    /// <summary>
    /// Highlight curve tone adjustment (-100 to +100). 0 is neutral.
    /// </summary>
    public double Highlights { get; set; } = 0.0;

    /// <summary>
    /// Shadow curve tone adjustment (-100 to +100). 0 is neutral.
    /// </summary>
    public double Shadows { get; set; } = 0.0;

    /// <summary>
    /// Parametric S-curve contrast strength (0.0 to 1.0). 0.0 is linear.
    /// </summary>
    public double SCurveStrength { get; set; } = 0.0;

    /// <summary>
    /// Full path to loaded .cube LUT file (if any).
    /// </summary>
    public string? LutFilePath { get; set; } = null;

    /// <summary>
    /// Display file name of loaded .cube LUT.
    /// </summary>
    public string? LutFileName { get; set; } = null;

    /// <summary>
    /// Whether the loaded LUT transform is active.
    /// </summary>
    public bool LutEnabled { get; set; } = false;

    /// <summary>
    /// Spatial unsharp-mask sharpening strength (0.0 to 1.0). 0.0 is disabled.
    /// </summary>
    public double SharpenStrength { get; set; } = 0.0;

    /// <summary>
    /// Spatial edge-preserving denoising strength (0.0 to 1.0). 0.0 is disabled.
    /// </summary>
    public double DenoiseStrength { get; set; } = 0.0;

    /// <summary>
    /// Software desaturation flag.
    /// </summary>
    [JsonIgnore]
    public bool SoftwareMonochrome
    {
        get => Saturation <= 0.001;
        set => Saturation = value ? 0.0 : 1.0;
    }

    /// <summary>
    /// Backward-compatibility property alias for Contrast.
    /// </summary>
    [JsonIgnore]
    public double ContrastBoost
    {
        get => Contrast;
        set => Contrast = value;
    }

    public SoftwareLookSettings Clone()
    {
        return new SoftwareLookSettings
        {
            Enabled = Enabled,
            LookName = LookName,
            ToneCurveName = ToneCurveName,
            ExposureOffset = ExposureOffset,
            Contrast = Contrast,
            Saturation = Saturation,
            Highlights = Highlights,
            Shadows = Shadows,
            SCurveStrength = SCurveStrength,
            LutFilePath = LutFilePath,
            LutFileName = LutFileName,
            LutEnabled = LutEnabled,
            SharpenStrength = SharpenStrength,
            DenoiseStrength = DenoiseStrength
        };
    }

    public void ResetToNeutral()
    {
        Enabled = true;
        LookName = "Neutral (Bypass)";
        ToneCurveName = "Linear (Passthrough)";
        ExposureOffset = 0.0;
        Contrast = 1.0;
        Saturation = 1.0;
        Highlights = 0.0;
        Shadows = 0.0;
        SCurveStrength = 0.0;
        LutFilePath = null;
        LutFileName = null;
        LutEnabled = false;
        SharpenStrength = 0.0;
        DenoiseStrength = 0.0;
    }

    public bool IsNeutral()
    {
        return Math.Abs(ExposureOffset) < 0.001 &&
               Math.Abs(Contrast - 1.0) < 0.001 &&
               Math.Abs(Saturation - 1.0) < 0.001 &&
               Math.Abs(Highlights) < 0.001 &&
               Math.Abs(Shadows) < 0.001 &&
               Math.Abs(SCurveStrength) < 0.001 &&
               (!LutEnabled || string.IsNullOrEmpty(LutFilePath)) &&
               SharpenStrength < 0.001 &&
               DenoiseStrength < 0.001;
    }
}

/// <summary>
/// A reusable, persistent capture profile encapsulating hardware camera controls
/// and forward-compatible software look parameters.
/// </summary>
public class CapturePreset
{
    public const int CurrentSchemaVersion = 3;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; } = false;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// List of target hardware control values.
    /// </summary>
    public List<PresetControlValue> HardwareControls { get; set; } = [];

    /// <summary>
    /// Software look parameters (Phase 7 extension point).
    /// </summary>
    public SoftwareLookSettings SoftwareLook { get; set; } = new();

    /// <summary>
    /// False-color exposure scope parameters (Phase 8 extension point).
    /// </summary>
    public FalseColorSettings FalseColor { get; set; } = new();

    /// <summary>
    /// Optional vendor-specific hardware ISP settings (Phase 15 extension point).
    /// Explicitly isolated from software look parameters.
    /// Only applies when a compatible Realtek camera is connected.
    /// </summary>
    public VendorHardwareSettings? VendorHardware { get; set; } = null;

    public CapturePreset Clone(string? newName = null)
    {
        var clone = new CapturePreset
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = newName ?? $"{Name} (Copy)",
            Description = Description,
            IsBuiltIn = false, // Clones are always user-editable
            SchemaVersion = SchemaVersion,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            HardwareControls = new List<PresetControlValue>(),
            SoftwareLook = SoftwareLook.Clone(),
            FalseColor = FalseColor.Clone(),
            VendorHardware = VendorHardware?.Clone()
        };

        foreach (var c in HardwareControls)
        {
            clone.HardwareControls.Add(new PresetControlValue
            {
                PropertyName = c.PropertyName,
                PropertyType = c.PropertyType,
                PropertyId = c.PropertyId,
                Value = c.Value,
                IsAuto = c.IsAuto
            });
        }

        return clone;
    }
}
