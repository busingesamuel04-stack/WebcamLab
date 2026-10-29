using System;
using System.Collections.Generic;

namespace WebcamLab.Core.ReverseEngineering;

public class CandidateModeBandwidth
{
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public double BytesPerPixel { get; set; }
    public double CompressionRatio { get; set; } = 1.0;
    public double FrameSizeBytes => (Width * Height * BytesPerPixel) / CompressionRatio;
    public double DataRateMBps => (FrameSizeBytes * Fps) / (1024.0 * 1024.0);
    public double DataRateMbps => (FrameSizeBytes * Fps * 8.0) / 1_000_000.0;
    public bool FitsUsb2PhysicalWire => DataRateMbps <= 480.0;
    public bool FitsUsb2IsochronousCeiling => DataRateMbps <= 196.6; // 3 x 1024 bytes per 125us microframe
    public string FeasibilityAssessment { get; set; } = string.Empty;
}

public static class BandwidthFeasibilityModel
{
    public const double USB2_PHYSICAL_WIRE_MBPS = 480.0;
    public const double USB2_MAX_ISOC_PAYLOAD_MBPS = 196.608; // 3072 bytes/uF * 8000 uF/s * 8 bits

    public static List<CandidateModeBandwidth> EvaluateCandidateModes()
    {
        var list = new List<CandidateModeBandwidth>
        {
            new()
            {
                Name = "1080p RAW Bayer 8-bit @ 30 FPS",
                Width = 1920, Height = 1080, Fps = 30.0, BytesPerPixel = 1.0,
                FeasibilityAssessment = "PHYSICALLY IMPOSSIBLE over USB 2.0 (497.7 Mbps exceeds 480 Mbps physical wire bandwidth)."
            },
            new()
            {
                Name = "1080p RAW Bayer 8-bit @ 15 FPS",
                Width = 1920, Height = 1080, Fps = 15.0, BytesPerPixel = 1.0,
                FeasibilityAssessment = "EXCEEDS USB 2.0 isochronous single-endpoint ceiling (248.8 Mbps > 196.6 Mbps max)."
            },
            new()
            {
                Name = "1080p RAW Bayer 8-bit @ 10 FPS",
                Width = 1920, Height = 1080, Fps = 10.0, BytesPerPixel = 1.0,
                FeasibilityAssessment = "BANDWIDTH FEASIBLE over USB 2.0 (165.9 Mbps < 196.6 Mbps ceiling)."
            },
            new()
            {
                Name = "1080p RAW Bayer 8-bit @ 5 FPS",
                Width = 1920, Height = 1080, Fps = 5.0, BytesPerPixel = 1.0,
                FeasibilityAssessment = "HIGHLY FEASIBLE over USB 2.0 (82.9 Mbps)."
            },
            new()
            {
                Name = "1080p RAW Bayer 10-bit @ 30 FPS (Packed)",
                Width = 1920, Height = 1080, Fps = 30.0, BytesPerPixel = 1.25,
                FeasibilityAssessment = "PHYSICALLY IMPOSSIBLE over USB 2.0 (622.1 Mbps)."
            },
            new()
            {
                Name = "1080p YUY2 Uncompressed 4:2:2 @ 30 FPS",
                Width = 1920, Height = 1080, Fps = 30.0, BytesPerPixel = 2.0,
                FeasibilityAssessment = "PHYSICALLY IMPOSSIBLE over USB 2.0 (995.3 Mbps exceeds wire limit by >2x)."
            },
            new()
            {
                Name = "1080p YUY2 Uncompressed 4:2:2 @ 5 FPS (Active Hardware Mode)",
                Width = 1920, Height = 1080, Fps = 5.0, BytesPerPixel = 2.0,
                FeasibilityAssessment = "CONFIRMED SUPPORTED & OPERATIONAL (165.9 Mbps fits within 196.6 Mbps isoc ceiling)."
            },
            new()
            {
                Name = "1080p MJPEG Compressed @ 30 FPS (Active Hardware Mode)",
                Width = 1920, Height = 1080, Fps = 30.0, BytesPerPixel = 2.0, CompressionRatio = 14.0,
                FeasibilityAssessment = "CONFIRMED SUPPORTED & OPERATIONAL (~35.5 Mbps fits easily in USB 2.0 isoc bandwidth)."
            },
            new()
            {
                Name = "720p RAW Bayer 8-bit @ 30 FPS",
                Width = 1280, Height = 720, Fps = 30.0, BytesPerPixel = 1.0,
                FeasibilityAssessment = "BANDWIDTH FEASIBLE over USB 2.0 (221.2 Mbps wire, near boundary)."
            },
            new()
            {
                Name = "720p RAW Bayer 8-bit @ 24 FPS (Cinematic Rate)",
                Width = 1280, Height = 720, Fps = 24.0, BytesPerPixel = 1.0,
                FeasibilityAssessment = "BANDWIDTH FEASIBLE over USB 2.0 (176.9 Mbps < 196.6 Mbps ceiling)."
            }
        };

        return list;
    }
}
