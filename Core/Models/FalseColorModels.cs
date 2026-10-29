using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebcamLab.Core.Models;

/// <summary>
/// Selectable false-color exposure reference profile.
/// </summary>
public enum FalseColorProfile
{
    /// <summary>
    /// Published SmallHD ARRI color-spectrum reference bands (Purple, Blue, Green, Pink, Yellow, Red)
    /// with intermediate ranges rendered as desaturated grayscale or pass-through.
    /// Labeled strictly as an ARRI-style reference; does not claim ARRI LogC or camera calibration.
    /// </summary>
    ArriStyleReference = 0,

    /// <summary>
    /// WebcamLab custom 10-zone profile spanning 0 to 100 IRE with contiguous, non-overlapping intervals.
    /// </summary>
    WebcamLabCustom10Zone = 1
}

/// <summary>
/// Source buffer selected for exposure diagnostics (False Color, Histogram, Clipping).
/// </summary>
public enum AnalysisSource
{
    /// <summary>
    /// Original decoded camera image (_bgr32Buffer). Default for exposure assistance to prevent
    /// software looks/curves from silently distorting exposure measurements.
    /// </summary>
    OriginalCameraFeed = 0,

    /// <summary>
    /// Graded preview image (_processedBuffer). Optional mode for inspecting the effect of an active look.
    /// </summary>
    GradedPreview = 1
}

/// <summary>
/// Appearance mode for intermediate (unmapped) ranges in ARRI-style false color.
/// </summary>
public enum ArriIntermediateMode
{
    /// <summary>
    /// Render intermediate ranges as desaturated grayscale (standard on cinema field monitors).
    /// </summary>
    DesaturatedGrayscale = 0,

    /// <summary>
    /// Retain original frame color for intermediate ranges.
    /// </summary>
    PassThroughColor = 1
}

/// <summary>
/// A single color zone within the false-color profile.
/// </summary>
public class FalseColorZone
{
    public int ZoneIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double MinIre { get; set; }
    public double MaxIre { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    [JsonIgnore]
    public string HexColor => $"#{R:X2}{G:X2}{B:X2}";

    [JsonIgnore]
    public uint Bgr32Color => 0xFF000000u | ((uint)R << 16) | ((uint)G << 8) | (uint)B;

    public FalseColorZone() { }

    public FalseColorZone(int index, string name, string description, double minIre, double maxIre, byte r, byte g, byte b)
    {
        ZoneIndex = index;
        Name = name;
        Description = description;
        MinIre = minIre;
        MaxIre = maxIre;
        R = r;
        G = g;
        B = b;
    }

    public FalseColorZone Clone()
    {
        return new FalseColorZone
        {
            ZoneIndex = ZoneIndex,
            Name = Name,
            Description = Description,
            MinIre = MinIre,
            MaxIre = MaxIre,
            R = R,
            G = G,
            B = B
        };
    }
}

/// <summary>
/// Configuration and persistent settings for Phase 8 False-Color Scopes.
/// </summary>
public class FalseColorSettings
{
    /// <summary>
    /// Master toggle for false-color exposure overlay on preview viewport.
    /// Defaults to false so camera capture begins in normal viewing mode.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Selected reference profile. Default is WebcamLab Custom 10-Zone.
    /// </summary>
    public FalseColorProfile SelectedProfile { get; set; } = FalseColorProfile.WebcamLabCustom10Zone;

    /// <summary>
    /// Source buffer analyzed. Defaults to Original Camera Feed.
    /// </summary>
    public AnalysisSource AnalysisSource { get; set; } = AnalysisSource.OriginalCameraFeed;

    /// <summary>
    /// Rendering mode for intermediate unmapped IRE bands in ARRI-style profile.
    /// </summary>
    public ArriIntermediateMode ArriIntermediateMode { get; set; } = ArriIntermediateMode.DesaturatedGrayscale;

    /// <summary>
    /// False-color overlay opacity (0.0 = completely transparent, 1.0 = solid false color).
    /// </summary>
    public double OverlayOpacity { get; set; } = 1.0;

    /// <summary>
    /// User-adjustable skin-tone reference target in IRE (default: 54.0 IRE).
    /// Not an automated judgment of correct skin appearance; provided as an exposure aid.
    /// </summary>
    public double SkinToneTarget { get; set; } = 54.0;

    /// <summary>
    /// Tolerance window for skin-tone reference target in IRE (default: ±3.0 IRE).
    /// </summary>
    public double SkinToneTolerance { get; set; } = 3.0;

    /// <summary>
    /// User-adjustable middle-gray reference target in IRE (default: 40.0 IRE).
    /// </summary>
    public double MiddleGrayTarget { get; set; } = 40.0;

    /// <summary>
    /// Tolerance window for middle-gray reference target in IRE (default: ±2.0 IRE).
    /// </summary>
    public double MiddleGrayTolerance { get; set; } = 2.0;

    /// <summary>
    /// Ten contiguous custom zones for the WebcamLab 10-Zone profile.
    /// </summary>
    public List<FalseColorZone> CustomZones { get; set; } = CreateDefault10Zones();

    /// <summary>
    /// Creates the standard default WebcamLab 10-Zone map covering 0 to 100 IRE contiguously.
    /// </summary>
    public static List<FalseColorZone> CreateDefault10Zones()
    {
        return
        [
            new(0, "Deep Shadow", "Crushed blacks and noise floor warning", 0.0, 2.0, 124, 58, 237),      // Deep Purple (#7C3AED)
            new(1, "Shadow Detail", "Low shadow detail and dark gradients", 2.0, 10.0, 37, 99, 235),       // Royal Blue (#2563EB)
            new(2, "Lower Midtones", "Dark scene elements, dark clothing, falloff", 10.0, 30.0, 13, 148, 136), // Teal (#0D9488)
            new(3, "Midtones", "General midtone scene values", 30.0, 38.0, 77, 124, 15),                    // Olive Green (#4D7C0F)
            new(4, "Middle Gray (18%)", "18% middle-gray card reference zone", 38.0, 44.0, 22, 163, 74),    // Green (#16A34A)
            new(5, "Upper Midtones", "Key skin tones and bright midtones", 44.0, 58.0, 251, 113, 133),      // Coral/Pink (#FB7185)
            new(6, "Highlight Detail", "High-key scene elements, sky and textiles", 58.0, 78.0, 6, 182, 212),// Light Cyan (#06B6D4)
            new(7, "High Highlight", "Bright highlights approaching knee roll-off", 78.0, 90.0, 245, 158, 11),// Amber/Orange (#F59E0B)
            new(8, "Near-Clipping", "Impending saturation warning", 90.0, 98.0, 234, 179, 8),               // Yellow (#EAB308)
            new(9, "Delivered Clipping", "Hardware/ISP delivered code-value clip", 98.0, 100.0, 220, 38, 38) // Bright Red (#DC2626)
        ];
    }

    /// <summary>
    /// Published SmallHD ARRI color-spectrum reference bands.
    /// Source: SmallHD User Guide, Exposure Assist (False Color).
    /// </summary>
    public static List<FalseColorZone> CreateArriReferenceZones()
    {
        return
        [
            new(0, "Purple (0–2.5 IRE)", "Deep underexposure / black clipping", 0.0, 2.5, 153, 0, 153),    // Purple (#990099)
            new(1, "Blue (2.5–4 IRE)", "Just above black clipping", 2.5, 4.0, 0, 0, 255),                  // Blue (#0000FF)
            new(2, "Green (38–42 IRE)", "18% neutral gray card reference", 38.0, 42.0, 0, 204, 0),         // Green (#00CC00)
            new(3, "Pink (52–56 IRE)", "Skin tone key exposure reference", 52.0, 56.0, 255, 105, 180),    // Pink (#FF69B4)
            new(4, "Yellow (97–99 IRE)", "Highlight warning approaching clip", 97.0, 99.0, 255, 255, 0),   // Yellow (#FFFF00)
            new(5, "Red (99–100 IRE)", "Delivered-pixel white clipping", 99.0, 100.0, 255, 0, 0)          // Red (#FF0000)
        ];
    }

    /// <summary>
    /// Validates a list of 10 zones for contiguous coverage from 0.0 to 100.0 IRE without gaps, overlaps, or inversions.
    /// </summary>
    public static bool ValidateCustomZones(List<FalseColorZone> zones, out string? errorMessage)
    {
        if (zones == null || zones.Count != 10)
        {
            errorMessage = $"Custom profile must contain exactly 10 zones. Current count: {zones?.Count ?? 0}.";
            return false;
        }

        const double Tolerance = 0.001;

        if (Math.Abs(zones[0].MinIre - 0.0) > Tolerance)
        {
            errorMessage = $"Zone 0 must start at 0.0 IRE. Current start: {zones[0].MinIre:F1} IRE.";
            return false;
        }

        if (Math.Abs(zones[9].MaxIre - 100.0) > Tolerance)
        {
            errorMessage = $"Zone 9 must terminate at 100.0 IRE. Current termination: {zones[9].MaxIre:F1} IRE.";
            return false;
        }

        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (zone.MinIre < 0.0 || zone.MaxIre > 100.0)
            {
                errorMessage = $"Zone {i} [{zone.Name}] contains out-of-range boundaries ({zone.MinIre:F1} - {zone.MaxIre:F1} IRE). Valid range is 0.0 to 100.0 IRE.";
                return false;
            }

            if (zone.MinIre >= zone.MaxIre)
            {
                errorMessage = $"Zone {i} [{zone.Name}] has an inverted or zero-width interval: Min ({zone.MinIre:F1}) >= Max ({zone.MaxIre:F1}).";
                return false;
            }

            if (i > 0)
            {
                var prevZone = zones[i - 1];
                if (Math.Abs(zone.MinIre - prevZone.MaxIre) > Tolerance)
                {
                    if (zone.MinIre < prevZone.MaxIre)
                    {
                        errorMessage = $"Overlap detected between Zone {i - 1} [{prevZone.Name}] and Zone {i} [{zone.Name}]: " +
                                       $"Zone {i - 1} ends at {prevZone.MaxIre:F1} IRE but Zone {i} starts at {zone.MinIre:F1} IRE.";
                    }
                    else
                    {
                        errorMessage = $"Gap detected between Zone {i - 1} [{prevZone.Name}] and Zone {i} [{zone.Name}]: " +
                                       $"Zone {i - 1} ends at {prevZone.MaxIre:F1} IRE but Zone {i} starts at {zone.MinIre:F1} IRE.";
                    }
                    return false;
                }
            }
        }

        errorMessage = null;
        return true;
    }

    public FalseColorSettings Clone()
    {
        var clone = new FalseColorSettings
        {
            Enabled = Enabled,
            SelectedProfile = SelectedProfile,
            AnalysisSource = AnalysisSource,
            ArriIntermediateMode = ArriIntermediateMode,
            OverlayOpacity = OverlayOpacity,
            SkinToneTarget = SkinToneTarget,
            SkinToneTolerance = SkinToneTolerance,
            MiddleGrayTarget = MiddleGrayTarget,
            MiddleGrayTolerance = MiddleGrayTolerance,
            CustomZones = new List<FalseColorZone>(CustomZones.Count)
        };

        foreach (var z in CustomZones)
        {
            clone.CustomZones.Add(z.Clone());
        }

        return clone;
    }

    public void ResetToDefaults()
    {
        Enabled = false;
        SelectedProfile = FalseColorProfile.WebcamLabCustom10Zone;
        AnalysisSource = AnalysisSource.OriginalCameraFeed;
        ArriIntermediateMode = ArriIntermediateMode.DesaturatedGrayscale;
        OverlayOpacity = 1.0;
        SkinToneTarget = 54.0;
        SkinToneTolerance = 3.0;
        MiddleGrayTarget = 40.0;
        MiddleGrayTolerance = 2.0;
        CustomZones = CreateDefault10Zones();
    }
}
