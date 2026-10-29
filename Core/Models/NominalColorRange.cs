namespace WebcamLab.Core.Models;

/// <summary>
/// Explicit, unambiguous nominal color range enumeration for video streams.
/// Maps directly to Windows Media Foundation MFNominalRange (mfobjects.h).
/// </summary>
public enum NominalColorRange
{
    /// <summary>
    /// Nominal range attribute not provided by driver/source (MFNominalRange_Unknown = 0).
    /// Standard convention: defaults to Full Range (0-255) for computer graphics / MJPEG,
    /// or Video Range (16-235) for standard broadcast YUV.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Full Range: Normalized [0..1] maps directly to code values [0..255].
    /// Corresponds to MFNominalRange_Normal (1) / MFNominalRange_0_255.
    /// Used by standard JPEG/sRGB and computer display pipelines.
    /// Thresholds: Shadow Crush &lt;= 1, Highlight Clip &gt;= 254.
    /// </summary>
    FullRange_0_255 = 1,

    /// <summary>
    /// Video / Studio Range: Normalized [0..1] maps to code values [16..235] for Luma (Y),
    /// reserving [0..15] for sync footroom and [236..255] for specular headroom.
    /// Corresponds to MFNominalRange_Wide (2) / MFNominalRange_16_235.
    /// Used by standard broadcast Rec.601 / Rec.709 uncompressed video.
    /// Thresholds: Shadow Crush &lt;= 16, Highlight Clip &gt;= 235.
    /// </summary>
    VideoRange_16_235 = 2,

    /// <summary>
    /// Extended 48-208 range (MFNominalRange_48_208 = 3).
    /// </summary>
    Extended_48_208 = 3,

    /// <summary>
    /// Extended 64-127 range (MFNominalRange_64_127 = 4).
    /// </summary>
    Extended_64_127 = 4
}
