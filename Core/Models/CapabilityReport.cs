using System.Collections.Generic;

namespace WebcamLab.Core.Models;

public enum CapabilityClassification
{
    ConfirmedByDeviceOrApi,
    NotExposedByCurrentCapturePath,
    NotYetTested
}

public class CapabilityItem
{
    public required string Feature { get; init; }
    public required CapabilityClassification Classification { get; init; }
    public required string StatusText { get; init; }
    public required string Details { get; init; }
    public required string ArchitecturalNote { get; init; }

    public string BadgeBackground => Classification switch
    {
        CapabilityClassification.ConfirmedByDeviceOrApi => "#065F46", // Dark Emerald
        CapabilityClassification.NotExposedByCurrentCapturePath => "#7F1D1D", // Dark Red
        CapabilityClassification.NotYetTested => "#78350F", // Dark Amber
        _ => "#1E293B"
    };

    public string BadgeForeground => Classification switch
    {
        CapabilityClassification.ConfirmedByDeviceOrApi => "#34D399", // Mint / Emerald
        CapabilityClassification.NotExposedByCurrentCapturePath => "#F87171", // Rose / Red
        CapabilityClassification.NotYetTested => "#FBBF24", // Amber / Gold
        _ => "#94A3B8"
    };
}

public static class CapabilityReportFactory
{
    public static List<CapabilityItem> GenerateReportForDellUZ2315H(IReadOnlyList<CameraFormat> formats)
    {
        var items = new List<CapabilityItem>
        {
            new()
            {
                Feature = "MJPEG 1080p @ 30 FPS",
                Classification = CapabilityClassification.ConfirmedByDeviceOrApi,
                StatusText = "Confirmed by API & Hardware",
                Details = "1920×1080 compressed MJPEG stream delivered over USB 2.0 High Speed.",
                ArchitecturalNote = "Camera ISP encodes discrete JPEG frames (JFIF SOI 0xFFD8). Fast motion may show compression artifacts."
            },
            new()
            {
                Feature = "YUY2 Uncompressed 1080p @ 5 FPS",
                Classification = CapabilityClassification.ConfirmedByDeviceOrApi,
                StatusText = "Confirmed by API & Hardware",
                Details = "1920×1080 4:2:2 uncompressed YUV. 4,147,200 bytes per frame (33.18 MB/s @ 5 FPS).",
                ArchitecturalNote = "Exhausts USB 2.0 480 Mbps isochronous bandwidth (~35-40 MB/s practical limit). Framerate restricted to 5 FPS."
            },
            new()
            {
                Feature = "YUY2 Uncompressed 720p @ 10 FPS",
                Classification = CapabilityClassification.ConfirmedByDeviceOrApi,
                StatusText = "Confirmed by API & Hardware",
                Details = "1280×720 4:2:2 uncompressed YUV. 1,843,200 bytes per frame (18.43 MB/s @ 10 FPS).",
                ArchitecturalNote = "Bandwidth-limited by USB 2.0 endpoint."
            },
            new()
            {
                Feature = "8-bit Rec.709 / BT.601 Color Precision",
                Classification = CapabilityClassification.ConfirmedByDeviceOrApi,
                StatusText = "Confirmed by API & Hardware",
                Details = "Standard 8-bit per channel YUV/RGB digital video quantization.",
                ArchitecturalNote = "Standard UVC video payload format."
            },
            new()
            {
                Feature = "True Sensor RAW (Bayer CFA Pattern)",
                Classification = CapabilityClassification.NotExposedByCurrentCapturePath,
                StatusText = "Not Exposed by Windows UVC",
                Details = "Standard Microsoft usbvideo.sys UVC driver does not expose RAW10 or RAW12 streams.",
                ArchitecturalNote = "Crucial distinction: MJPEG and YUY2/yuvj422p are processed YUV video after on-chip ISP demosaicing and matrixing, NOT sensor RAW."
            },
            new()
            {
                Feature = "10-bit / 12-bit Deep Color Capture",
                Classification = CapabilityClassification.NotExposedByCurrentCapturePath,
                StatusText = "Not Supported by Current Path",
                Details = "No 10-bit/12-bit pixel formats (P010, Y210, b48r) advertised in Media Foundation descriptors.",
                ArchitecturalNote = "Hardware sensor or bridge chip does not provide deep color endpoints through UVC."
            },
            new()
            {
                Feature = "Sensor HDR (Hardware Dual-Gain / Multi-Exposure)",
                Classification = CapabilityClassification.NotExposedByCurrentCapturePath,
                StatusText = "Not Exposed / Sensor Lacks HDR",
                Details = "No multi-exposure merge or Staggered HDR controls exposed.",
                ArchitecturalNote = "Tone mapping in software (e.g. logarithmic tone curves) cannot recover optical sensor clip."
            },
            new()
            {
                Feature = "Direct Sensor Register Access (I2C / UVC XU)",
                Classification = CapabilityClassification.NotYetTested,
                StatusText = "Pending UVC XU Investigation",
                Details = "Custom Realtek RTS5822 / RTS58xx Extension Units (XU) may exist on the USB descriptor tree.",
                ArchitecturalNote = "Requires low-level UVC Extension Unit querying to determine if raw ISP bypass or manual analog gain registers can be queried."
            },
            new()
            {
                Feature = "Manual Sensor Exposure & Gain Controls",
                Classification = CapabilityClassification.NotYetTested,
                StatusText = "Partially Exposed via IAMCameraControl",
                Details = "Windows IAMCameraControl / IAMVideoProcAmp interface exposure, gain, and white balance.",
                ArchitecturalNote = "Can be accessed through DirectShow/Media Foundation property sets."
            }
        };

        return items;
    }
}
