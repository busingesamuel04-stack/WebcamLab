using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WebcamLab.Capture;
using WebcamLab.Core.Models;

namespace WebcamLab.Core.ReverseEngineering;

public class EndpointBandwidthProfile
{
    public byte AltSetting { get; set; }
    public byte EndpointAddress { get; set; }
    public ushort RawMaxPacketSize { get; set; }
    public int PacketSize { get; set; }
    public int Multiplier { get; set; }
    public int BytesPerMicroframe { get; set; }
    public double BandwidthMbps { get; set; }
}

public class FormatFrameProfile
{
    public string FormatName { get; set; } = string.Empty;
    public byte FormatIndex { get; set; }
    public byte FrameIndex { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public uint FrameSizeBytes { get; set; }
    public double DefaultFps { get; set; }
    public List<double> AdvertisedFpsList { get; set; } = [];
    public int RequiredMicroframesPerFrame { get; set; }
    public double RequiredMbpsAtMaxFps { get; set; }
    public bool FitsWithinMaxIsochronous { get; set; }
}

public class LiveDeliveryAuditResult
{
    public string FormatName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double RequestedFps { get; set; }
    public int TotalFramesCaptured { get; set; }
    public int UniqueFramesCount { get; set; }
    public int DuplicateFramesCount { get; set; }
    public double AchievedFps { get; set; }
    public double UniqueFps { get; set; }
    public double AverageDeltaMs { get; set; }
    public double MinDeltaMs { get; set; }
    public double MaxDeltaMs { get; set; }
    public double JitterMs { get; set; }
    public List<double> FrameIntervalsMs { get; set; } = [];
}

public static class BandwidthAndFrameDeliveryAuditor
{
    public static (List<EndpointBandwidthProfile> Endpoints, List<FormatFrameProfile> Frames) ParseDescriptorPhysics(byte[] descBytes)
    {
        var endpoints = new List<EndpointBandwidthProfile>();
        var frames = new List<FormatFrameProfile>();

        int offset = 0;
        byte curIf = 0;
        byte curAlt = 0;
        byte curSubClass = 0;
        string curFormatName = string.Empty;
        byte curFormatIdx = 0;

        while (offset < descBytes.Length)
        {
            if (offset + 1 >= descBytes.Length) break;
            byte len = descBytes[offset];
            byte type = descBytes[offset + 1];
            if (len == 0 || offset + len > descBytes.Length) break;

            if (type == 4) // Interface
            {
                curIf = descBytes[offset + 2];
                curAlt = descBytes[offset + 3];
                curSubClass = descBytes[offset + 6];
            }
            else if (type == 5 && curIf == 1) // Endpoint in VideoStreaming
            {
                byte epAddr = descBytes[offset + 2];
                ushort maxPkt = BitConverter.ToUInt16(descBytes, offset + 4);
                int pkt = maxPkt & 0x7FF;
                int mult = ((maxPkt >> 11) & 0x03) + 1;
                int totalB = pkt * mult;
                double mbps = (totalB * 8000.0 * 8.0) / 1_000_000.0;

                endpoints.Add(new EndpointBandwidthProfile
                {
                    AltSetting = curAlt,
                    EndpointAddress = epAddr,
                    RawMaxPacketSize = maxPkt,
                    PacketSize = pkt,
                    Multiplier = mult,
                    BytesPerMicroframe = totalB,
                    BandwidthMbps = mbps
                });
            }
            else if (type == 0x24 && curSubClass == 2) // CS_INTERFACE in VideoStreaming
            {
                byte subType = descBytes[offset + 2];
                if (subType == 4) // VS_FORMAT_UNCOMPRESSED
                {
                    curFormatIdx = descBytes[offset + 3];
                    curFormatName = "YUY2 (Uncompressed 4:2:2)";
                }
                else if (subType == 6) // VS_FORMAT_MJPEG
                {
                    curFormatIdx = descBytes[offset + 3];
                    curFormatName = "MJPEG (Compressed)";
                }
                else if (subType == 5 || subType == 7) // VS_FRAME_UNCOMPRESSED or VS_FRAME_MJPEG
                {
                    byte frIdx = descBytes[offset + 3];
                    ushort w = BitConverter.ToUInt16(descBytes, offset + 5);
                    ushort h = BitConverter.ToUInt16(descBytes, offset + 7);
                    uint defInt = BitConverter.ToUInt32(descBytes, offset + 21);
                    double defFps = defInt > 0 ? 10_000_000.0 / defInt : 0;
                    byte intType = descBytes[offset + 25];

                    var fpsList = new List<double>();
                    for (int i = 0; i < intType; i++)
                    {
                        uint fInt = BitConverter.ToUInt32(descBytes, offset + 26 + (i * 4));
                        if (fInt > 0) fpsList.Add(10_000_000.0 / fInt);
                    }

                    uint frameBytes = (uint)(w * h * 2); // 2 bytes per pixel for YUY2
                    int uFramesNeeded = (int)Math.Ceiling((double)frameBytes / 3072.0); // Assuming AltSetting 7 (3072 B/uF)
                    double maxFps = fpsList.Count > 0 ? fpsList[0] : defFps;
                    double requiredMbps = (frameBytes * 8.0 * maxFps) / 1_000_000.0;

                    frames.Add(new FormatFrameProfile
                    {
                        FormatName = curFormatName,
                        FormatIndex = curFormatIdx,
                        FrameIndex = frIdx,
                        Width = w,
                        Height = h,
                        FrameSizeBytes = frameBytes,
                        DefaultFps = defFps,
                        AdvertisedFpsList = fpsList,
                        RequiredMicroframesPerFrame = uFramesNeeded,
                        RequiredMbpsAtMaxFps = requiredMbps,
                        FitsWithinMaxIsochronous = requiredMbps <= 196.608
                    });
                }
            }

            offset += len;
        }

        return (endpoints, frames);
    }

    /// <summary>
    /// Performs live frame delivery and frame-uniqueness audit using the physical camera.
    /// </summary>
    public static async Task<LiveDeliveryAuditResult> AuditLiveStreamDeliveryAsync(
        ICameraService cameraService,
        CameraDevice device,
        string formatTag,
        int width,
        int height,
        int sampleFrameCount = 40)
    {
        var result = new LiveDeliveryAuditResult
        {
            FormatName = formatTag,
            Width = width,
            Height = height
        };

        var formats = await cameraService.GetSupportedFormatsAsync(device).ConfigureAwait(false);
        var format = System.Linq.Enumerable.FirstOrDefault(formats, f =>
            f.FourCC.Equals(formatTag, StringComparison.OrdinalIgnoreCase) &&
            f.Width == width && f.Height == height);

        if (format == null) return result;

        result.RequestedFps = format.FrameRate;

        var timestamps = new List<long>();
        var checksums = new List<ulong>();
        var frameIntervals = new List<double>();
        var frameReceivedEvent = new ManualResetEventSlim(false);
        int received = 0;

        void OnFrame(IntPtr buf, int w, int h, int strd)
        {
            if (received >= sampleFrameCount) return;

            long ts = Stopwatch.GetTimestamp() * 10_000_000L / Stopwatch.Frequency;
            ulong cs = ComputeSampleChecksum(buf, strd, w, h);

            timestamps.Add(ts);
            checksums.Add(cs);
            received++;

            if (received >= sampleFrameCount) frameReceivedEvent.Set();
        }

        cameraService.FrameReady += OnFrame;

        try
        {
            await cameraService.StartCaptureAsync(device, format).ConfigureAwait(false);

            // Wait until sampleFrameCount frames collected or 10s timeout
            var sw = Stopwatch.StartNew();
            while (received < sampleFrameCount && sw.ElapsedMilliseconds < 12000)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }

            await cameraService.StopCaptureAsync().ConfigureAwait(false);
        }
        finally
        {
            cameraService.FrameReady -= OnFrame;
        }

        result.TotalFramesCaptured = timestamps.Count;
        if (timestamps.Count < 2) return result;

        // Compute deltas and count unique frames
        int uniqueCount = 1;
        int duplicateCount = 0;

        for (int i = 1; i < timestamps.Count; i++)
        {
            double deltaMs = (timestamps[i] - timestamps[i - 1]) / 10_000.0; // 100ns units to ms
            frameIntervals.Add(deltaMs);

            if (checksums[i] == checksums[i - 1])
            {
                duplicateCount++;
            }
            else
            {
                uniqueCount++;
            }
        }

        result.UniqueFramesCount = uniqueCount;
        result.DuplicateFramesCount = duplicateCount;
        result.FrameIntervalsMs = frameIntervals;

        double totalMs = (timestamps[^1] - timestamps[0]) / 10_000.0;
        result.AchievedFps = totalMs > 0 ? ((timestamps.Count - 1) * 1000.0) / totalMs : 0;
        result.UniqueFps = totalMs > 0 ? (uniqueCount * 1000.0) / totalMs : 0;

        if (frameIntervals.Count > 0)
        {
            double sum = 0;
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (var d in frameIntervals)
            {
                sum += d;
                if (d < min) min = d;
                if (d > max) max = d;
            }
            result.AverageDeltaMs = sum / frameIntervals.Count;
            result.MinDeltaMs = min;
            result.MaxDeltaMs = max;

            double sumSqDiff = 0;
            foreach (var d in frameIntervals)
            {
                double diff = d - result.AverageDeltaMs;
                sumSqDiff += diff * diff;
            }
            result.JitterMs = Math.Sqrt(sumSqDiff / frameIntervals.Count);
        }

        return result;
    }

    private static unsafe ulong ComputeSampleChecksum(IntPtr buffer, int stride, int width, int height)
    {
        if (buffer == IntPtr.Zero || width <= 0 || height <= 0) return 0;

        // Sample 32 representative lines across the image to detect inter-frame motion/noise
        ulong hash = 14695981039346656037UL; // FNV-1a offset basis
        byte* pBase = (byte*)buffer.ToPointer();

        int stepY = Math.Max(1, height / 32);
        for (int y = 0; y < height; y += stepY)
        {
            uint* pLine = (uint*)(pBase + (y * stride));
            // Sample 16 pixels per line
            int stepX = Math.Max(1, width / 16);
            for (int x = 0; x < width; x += stepX)
            {
                uint val = pLine[x];
                hash ^= val;
                hash *= 1099511628211UL; // FNV prime
            }
        }

        return hash;
    }
}
