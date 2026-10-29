using System;

namespace WebcamLab.Core.Models;

public class CameraFormat
{
    public required uint StreamIndex { get; init; }
    public required uint TypeIndex { get; init; }
    public required Guid SubtypeGuid { get; init; }
    public required string FourCC { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required double FrameRate { get; init; }
    public required uint FrameRateNumerator { get; init; }
    public required uint FrameRateDenominator { get; init; }
    public uint? RawNominalRangeValue { get; init; }
    public NominalColorRange NominalRange { get; init; } = NominalColorRange.Unspecified;

    public string NominalRangeText => NominalRange switch
    {
        NominalColorRange.FullRange_0_255 => $"Full Range [0..255]{(RawNominalRangeValue.HasValue ? $" (Raw: {RawNominalRangeValue.Value})" : "")}",
        NominalColorRange.VideoRange_16_235 => $"Video Range [16..235]{(RawNominalRangeValue.HasValue ? $" (Raw: {RawNominalRangeValue.Value})" : "")}",
        NominalColorRange.Extended_48_208 => $"Extended Range [48..208]{(RawNominalRangeValue.HasValue ? $" (Raw: {RawNominalRangeValue.Value})" : "")}",
        NominalColorRange.Extended_64_127 => $"Extended Range [64..127]{(RawNominalRangeValue.HasValue ? $" (Raw: {RawNominalRangeValue.Value})" : "")}",
        _ => RawNominalRangeValue.HasValue ? $"Unknown (Raw: {RawNominalRangeValue.Value})" : "Unspecified"
    };

    public string FormatCategory => FourCC switch
    {
        "MJPG" or "MJPEG" => "Compressed Motion-JPEG (Processed YUV)",
        "YUY2" or "YUYV" => "Uncompressed YUV 4:2:2 (Processed YUV)",
        "NV12" => "Uncompressed Bi-planar YUV 4:2:0 (Processed YUV)",
        "RGB24" or "RGB32" => "Uncompressed RGB",
        _ => "Custom Video Format"
    };

    public bool IsUncompressed => FourCC is "YUY2" or "YUYV" or "NV12" or "RGB24" or "RGB32";

    public bool IsSensorRaw => false;

    public string RawEvidenceNote => "Processed camera ISP output (YUV/JPEG). Not sensor RAW Bayer data.";

    public string ResolutionText => $"{Width} × {Height}";

    public string FrameRateText => $"{FrameRate:F2} FPS ({FrameRateNumerator}/{FrameRateDenominator})";

    public string DisplayString => $"{ResolutionText} @ {FrameRate:F0} FPS [{FourCC} - {FormatCategory}]";

    public override string ToString() => DisplayString;
}
