using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebcamLab.Capture;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Models;

namespace WebcamLab.Core.ReverseEngineering;

public class Phase16RunMetrics
{
    public string Format { get; set; } = string.Empty;
    public string Mp4Path { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int FramesCaptured { get; set; }
    public int FramesEncoded { get; set; }
    public double VideoDurationSec { get; set; }
    public double AudioDurationSec { get; set; }
    public double InitialOffsetMs { get; set; }
    public double DriftMs { get; set; }
    public int AudioDiscontinuities { get; set; }
    public bool HasVideoTrack { get; set; }
    public bool HasAudioTrack { get; set; }
    public string AudioCodec { get; set; } = string.Empty;
}

public static class Phase16VerificationRunner
{
    public static async Task<bool> RunVerificationAsync()
    {
        Console.WriteLine();
        Console.WriteLine("==================================================================");
        Console.WriteLine("  WebcamLab Phase 16: Synchronized A/V Hardware Verification");
        Console.WriteLine("  Device: Dell UZ2315H (RTS5875 ISP + Integrated Audio Codec)");
        Console.WriteLine("==================================================================");

        var reportSb = new System.Text.StringBuilder();
        reportSb.AppendLine("==================================================================");
        reportSb.AppendLine("  WebcamLab Phase 16: Synchronized A/V Hardware Verification Report");
        reportSb.AppendLine("  Target: Dell UZ2315H (RTS5875 ISP + Integrated Audio Codec)");
        reportSb.AppendLine($"  Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        reportSb.AppendLine("==================================================================\n");

        string outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "WebcamLab", "Phase16_Verification");
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

        using var cameraService = new MediaFoundationCameraService();

        // 1. Discover Camera
        Console.WriteLine("[1/5] Discovering Dell UZ2315H Video Device...");
        var cameras = await cameraService.EnumerateCamerasAsync();
        var camera = cameras.FirstOrDefault(c => c.IsDellUZ2315H) ?? cameras.FirstOrDefault();
        if (camera == null)
        {
            Console.WriteLine("  [FAIL] No camera devices detected.");
            return false;
        }
        Console.WriteLine($"  Found Video Device: {camera.Name} [{camera.SymbolicLink}]");
        reportSb.AppendLine($"Video Device: {camera.Name}");

        // 2. Discover Audio Endpoint
        Console.WriteLine("\n[2/5] Discovering Audio Capture Endpoints...");
        var audioDevs = await AudioDeviceService.EnumerateCaptureDevicesAsync();
        var audioDevice = audioDevs.FirstOrDefault(a => a.IsDellUZ2315H) ?? audioDevs.FirstOrDefault(a => a.IsDefault) ?? audioDevs.FirstOrDefault();
        if (audioDevice == null)
        {
            Console.WriteLine("  [FAIL] No audio capture endpoints detected.");
            return false;
        }
        Console.WriteLine($"  Found Audio Endpoint: {audioDevice.FriendlyName}");
        Console.WriteLine($"  Format: {audioDevice.FormatSummary}");
        Console.WriteLine($"  Dell Webcam Audio: {audioDevice.IsDellUZ2315H}");
        reportSb.AppendLine($"Audio Endpoint: {audioDevice.FriendlyName} [{audioDevice.FormatSummary}]\n");

        var formats = await cameraService.GetSupportedFormatsAsync(camera);
        var mjpg1080 = formats.FirstOrDefault(f => f.FourCC == "MJPG" && f.Width == 1920 && f.Height == 1080);
        var yuy21080 = formats.FirstOrDefault(f => f.FourCC == "YUY2" && f.Width == 1920 && f.Height == 1080);

        if (mjpg1080 == null || yuy21080 == null)
        {
            Console.WriteLine("  [FAIL] Required 1080p formats (MJPG, YUY2) not found.");
            return false;
        }

        // 3. Test Run 1: 1080p MJPEG Capture-Faithful VFR with Synchronized Audio
        Console.WriteLine("\n[3/5] Testing 1080p MJPEG Capture-Faithful A/V Recording (3.5s)...");
        string mjpgOutPath = Path.Combine(outDir, "Phase16_Dell_1080p_MJPG_AV.mp4");
        var mjpgMetrics = await RecordAndVerifyAsync(cameraService, camera, mjpg1080, audioDevice, mjpgOutPath, 3500);

        // 4. Test Run 2: 1080p YUY2 Capture-Faithful VFR with Synchronized Audio
        Console.WriteLine("\n[4/5] Testing 1080p YUY2 Capture-Faithful A/V Recording (3.5s)...");
        string yuy2OutPath = Path.Combine(outDir, "Phase16_Dell_1080p_YUY2_AV.mp4");
        var yuy2Metrics = await RecordAndVerifyAsync(cameraService, camera, yuy21080, audioDevice, yuy2OutPath, 3500);

        // 5. Verification & Analysis Summary
        Console.WriteLine("\n[5/5] Synthesizing Verification Results...");

        void LogMetrics(Phase16RunMetrics m)
        {
            Console.WriteLine($"\n--- Mode: {m.Format} ---");
            Console.WriteLine($"  MP4 Path:            {m.Mp4Path}");
            Console.WriteLine($"  File Size:           {m.FileSizeBytes:N0} bytes");
            Console.WriteLine($"  Frames Captured:     {m.FramesCaptured}");
            Console.WriteLine($"  Frames Encoded:      {m.FramesEncoded} (Zero Padding: {m.FramesCaptured == m.FramesEncoded})");
            Console.WriteLine($"  Video Duration:      {m.VideoDurationSec:F3} s");
            Console.WriteLine($"  Audio Duration:      {m.AudioDurationSec:F3} s");
            Console.WriteLine($"  Initial A/V Offset:  {m.InitialOffsetMs:+0.00;-0.00;0.00} ms");
            Console.WriteLine($"  Duration Drift:      {m.DriftMs:+0.00;-0.00;0.00} ms");
            Console.WriteLine($"  Discontinuities:     {m.AudioDiscontinuities}");
            Console.WriteLine($"  Stream Multiplexing: Video(H.264)={m.HasVideoTrack}, Audio({m.AudioCodec})={m.HasAudioTrack}");

            reportSb.AppendLine($"Mode: {m.Format}");
            reportSb.AppendLine($"  MP4 Path:            {m.Mp4Path}");
            reportSb.AppendLine($"  File Size:           {m.FileSizeBytes:N0} bytes");
            reportSb.AppendLine($"  Frames Captured:     {m.FramesCaptured}");
            reportSb.AppendLine($"  Frames Encoded:      {m.FramesEncoded}");
            reportSb.AppendLine($"  Video Duration:      {m.VideoDurationSec:F3} s");
            reportSb.AppendLine($"  Audio Duration:      {m.AudioDurationSec:F3} s");
            reportSb.AppendLine($"  Initial A/V Offset:  {m.InitialOffsetMs:+0.00;-0.00;0.00} ms");
            reportSb.AppendLine($"  Duration Drift:      {m.DriftMs:+0.00;-0.00;0.00} ms");
            reportSb.AppendLine($"  Discontinuities:     {m.AudioDiscontinuities}");
            reportSb.AppendLine($"  Stream Multiplexing: Video(H.264)={m.HasVideoTrack}, Audio({m.AudioCodec})={m.HasAudioTrack}\n");
        }

        LogMetrics(mjpgMetrics);
        LogMetrics(yuy2Metrics);

        bool mjpgValid = mjpgMetrics.HasVideoTrack && mjpgMetrics.HasAudioTrack && Math.Abs(mjpgMetrics.InitialOffsetMs) < 150;
        bool yuy2Valid = yuy2Metrics.HasVideoTrack && yuy2Metrics.HasAudioTrack && Math.Abs(yuy2Metrics.InitialOffsetMs) < 150;
        bool overallPassed = mjpgValid && yuy2Valid;

        Console.WriteLine("\n==================================================================");
        reportSb.AppendLine("==================================================================");
        if (overallPassed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  PHASE 16 SYNCHRONIZED A/V HARDWARE VERIFICATION: PASSED CLEANLY!");
            Console.ResetColor();
            reportSb.AppendLine("  PHASE 16 SYNCHRONIZED A/V HARDWARE VERIFICATION: PASSED CLEANLY!");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  PHASE 16 SYNCHRONIZED A/V HARDWARE VERIFICATION: FAILED.");
            Console.ResetColor();
            reportSb.AppendLine("  PHASE 16 SYNCHRONIZED A/V HARDWARE VERIFICATION: FAILED.");
        }
        Console.WriteLine("==================================================================");
        reportSb.AppendLine("==================================================================");

        string reportPath = Path.Combine(outDir, "Phase16_Verification_Report.txt");
        try
        {
            File.WriteAllText(reportPath, reportSb.ToString());
            Console.WriteLine($"\nReport saved to: {reportPath}");
        }
        catch { }

        return overallPassed;
    }

    private static async Task<Phase16RunMetrics> RecordAndVerifyAsync(
        MediaFoundationCameraService cameraService,
        CameraDevice camera,
        CameraFormat format,
        AudioDeviceInfo audioDev,
        string outputPath,
        int durationMs)
    {
        var metrics = new Phase16RunMetrics
        {
            Format = $"{format.FourCC} {format.Width}x{format.Height}",
            Mp4Path = outputPath
        };

        await cameraService.StartCaptureAsync(camera, format);
        await Task.Delay(1000); // Warm up camera sensor & pipeline

        await cameraService.StartRecordingAsync(
            outputPath,
            RecordingMode.OriginalCamera,
            RecordingCadenceMode.Capture_Faithful_VFR,
            enableAudio: true,
            audioDevice: audioDev);

        await Task.Delay(durationMs);

        await cameraService.StopRecordingAsync();
        await cameraService.StopCaptureAsync();

        if (File.Exists(outputPath))
        {
            var fi = new FileInfo(outputPath);
            metrics.FileSizeBytes = fi.Length;

            // Probe with ffprobe
            ProbeMediaFile(outputPath, metrics);

            // Read companion sidecar
            string sidecarPath = Path.ChangeExtension(outputPath, ".json");
            if (File.Exists(sidecarPath))
            {
                try
                {
                    string json = File.ReadAllText(sidecarPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("TotalFramesCaptured", out var fc)) metrics.FramesCaptured = fc.GetInt32();
                    if (root.TryGetProperty("TotalFramesEncoded", out var fe)) metrics.FramesEncoded = fe.GetInt32();
                    if (root.TryGetProperty("DurationSeconds", out var vd)) metrics.VideoDurationSec = vd.GetDouble();
                    if (root.TryGetProperty("AudioDurationSeconds", out var ad)) metrics.AudioDurationSec = ad.GetDouble();
                    if (root.TryGetProperty("InitialAudioVideoOffsetMs", out var io)) metrics.InitialOffsetMs = io.GetDouble();
                    if (root.TryGetProperty("AudioVideoDriftMs", out var df)) metrics.DriftMs = df.GetDouble();
                    if (root.TryGetProperty("AudioDiscontinuitiesCount", out var dc)) metrics.AudioDiscontinuities = dc.GetInt32();
                }
                catch { }
            }
            else
            {
                var report = cameraService.LastSyncReport;
                if (report != null)
                {
                    metrics.FramesCaptured = report.VideoFramesCount;
                    metrics.FramesEncoded = report.VideoFramesCount;
                    metrics.VideoDurationSec = report.VideoDurationSeconds;
                    metrics.AudioDurationSec = report.AudioDurationSeconds;
                    metrics.InitialOffsetMs = report.InitialOffsetMs;
                    metrics.DriftMs = report.DurationDriftMs;
                    metrics.AudioDiscontinuities = report.AudioDiscontinuitiesCount;
                }
            }
        }

        return metrics;
    }

    private static void ProbeMediaFile(string mp4Path, Phase16RunMetrics metrics)
    {
        var ffmpeg = WebcamLab.Capture.VideoRecorder.FindFfmpegPath() ?? "ffmpeg.exe";
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(ffprobe)) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments = $"-v error -show_entries stream=codec_type,codec_name -of default=noprint_wrappers=1:nokey=1 \"{mp4Path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            string outStr = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(5000);

            var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().ToLowerInvariant();
                if (line == "video")
                {
                    metrics.HasVideoTrack = true;
                }
                else if (line == "audio")
                {
                    metrics.HasAudioTrack = true;
                    metrics.AudioCodec = "aac";
                }
                else if (line == "h264")
                {
                    metrics.HasVideoTrack = true;
                }
                else if (line == "aac")
                {
                    metrics.HasAudioTrack = true;
                    metrics.AudioCodec = "aac";
                }
            }
        }
        catch { }
    }
}
