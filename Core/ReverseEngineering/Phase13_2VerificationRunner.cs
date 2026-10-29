using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebcamLab.Capture;
using WebcamLab.Core.Models;

namespace WebcamLab.Core.ReverseEngineering;

/// <summary>
/// Phase 13.2 True Source Timestamp Propagation Independent Verification Runner.
/// Performs rigorous physical hardware validation of true source-to-output timestamp propagation,
/// comparing Media Foundation IMFSample::GetSampleTime against decoded MP4 FFprobe pkt_pts_time.
/// Audits VFR vs CFR across 1080p MJPEG and 1080p YUY2, in both Original and Graded pipelines.
/// Quantifies per-frame MAE, Median, Max, 95th percentile error, duration parity, and zero-padding invariants.
/// </summary>
public static class Phase13_2VerificationRunner
{
    public class StreamProbeResult
    {
        public int FrameCount { get; set; }
        public double DurationSeconds { get; set; }
        public double AverageFps { get; set; }
        public double NominalTbr { get; set; }
        public List<double> PtsTimes { get; set; } = [];
        public List<double> Durations { get; set; } = [];
        public double MinDeltaMs { get; set; }
        public double MaxDeltaMs { get; set; }
        public double MeanDeltaMs { get; set; }
    }

    public class TimingStatistics
    {
        public double MaeMs { get; set; }
        public double MedianMs { get; set; }
        public double MaxDeltaMs { get; set; }
        public double P95Ms { get; set; }
        public double DurationErrorMs { get; set; }
        public int MonotonicClamps { get; set; }
        public int FrameCountDiscrepancy { get; set; }
    }

    public static async Task<bool> RunInvestigationAsync()
    {
        var sb = new StringBuilder();
        void Log(string s) { Console.WriteLine(s); sb.AppendLine(s); }

        Log("==================================================================");
        Log("  WebcamLab Phase 13.2: True Source Timestamp Propagation Audit");
        Log("  Hardware: Dell UZ2315H Monitor Integrated Webcam (0BDA:58BA)");
        Log("  Objective: Hardware Sample Time (100ns) -> MP4 Container PTS Parity");
        Log("==================================================================");

        string outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab", "Phase13_2");
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        using var cameraService = new MediaFoundationCameraService();
        var cameras = await cameraService.EnumerateCamerasAsync().ConfigureAwait(false);
        var camera = cameras.Count > 0 ? cameras[0] : null;

        if (camera == null)
        {
            Log("  [FAIL] No camera device discovered.");
            return false;
        }

        Log($"  [PASS] Selected camera: {camera.Name} ({camera.SymbolicLink})");
        var formats = await cameraService.GetSupportedFormatsAsync(camera).ConfigureAwait(false);

        CameraFormat? mjpeg1080 = null;
        CameraFormat? yuy21080 = null;

        foreach (var f in formats)
        {
            if (f.Width == 1920 && f.Height == 1080)
            {
                if (f.FourCC is "MJPG" or "MJPEG" && mjpeg1080 == null) mjpeg1080 = f;
                else if (f.FourCC is "YUY2" or "YUYV" && yuy21080 == null) yuy21080 = f;
            }
        }

        if (mjpeg1080 == null)
        {
            Log("  [FAIL] 1080p MJPEG format not available.");
            return false;
        }

        Log($"  [PASS] 1080p MJPEG Format: {mjpeg1080.Width}x{mjpeg1080.Height} @ {mjpeg1080.FrameRate} FPS ({mjpeg1080.FourCC})");
        if (yuy21080 != null)
        {
            Log($"  [PASS] 1080p YUY2 Format:  {yuy21080.Width}x{yuy21080.Height} @ {yuy21080.FrameRate} FPS ({yuy21080.FourCC})");
        }

        // =========================================================================
        // TEST 1: 1080p MJPEG - Capture-Faithful VFR (Original Camera)
        // =========================================================================
        Log("\n==================================================================");
        Log("  TEST 1: 1080p MJPEG Capture-Faithful VFR (Original Camera)");
        Log("==================================================================");

        await cameraService.StartCaptureAsync(camera, mjpeg1080).ConfigureAwait(false);
        Log("  Waiting 1.5s for camera auto-exposure settlement...");
        await Task.Delay(1500).ConfigureAwait(false);

        string mjpegVfrPath = Path.Combine(outputDir, $"WebcamLab_Phase13_2_MJPEG_VFR_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        await cameraService.StartRecordingAsync(mjpegVfrPath, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR).ConfigureAwait(false);
        await Task.Delay(3500).ConfigureAwait(false);
        await cameraService.StopRecordingAsync().ConfigureAwait(false);

        var mjpegVfrAudit = cameraService.GetLastRecordingAuditRecords();
        string mjpegVfrCsv = Path.ChangeExtension(mjpegVfrPath, ".timing.csv");
        cameraService.WriteTimingAuditSidecar(mjpegVfrCsv);

        var probeMjpegVfr = ProbeMp4Stream(mjpegVfrPath);
        LogStreamAudit("1080p MJPEG VFR (Original)", mjpegVfrPath, mjpegVfrAudit, probeMjpegVfr, RecordingCadenceMode.Capture_Faithful_VFR, Log);

        // =========================================================================
        // TEST 2: 1080p MJPEG - CFR Compatibility Mode (Original Camera)
        // =========================================================================
        Log("\n==================================================================");
        Log("  TEST 2: 1080p MJPEG CFR Compatibility Mode (Original Camera)");
        Log("==================================================================");

        string mjpegCfrPath = Path.Combine(outputDir, $"WebcamLab_Phase13_2_MJPEG_CFR_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        await cameraService.StartRecordingAsync(mjpegCfrPath, RecordingMode.OriginalCamera, RecordingCadenceMode.CFR_Compatibility).ConfigureAwait(false);
        await Task.Delay(3500).ConfigureAwait(false);
        await cameraService.StopRecordingAsync().ConfigureAwait(false);

        var mjpegCfrAudit = cameraService.GetLastRecordingAuditRecords();
        string mjpegCfrCsv = Path.ChangeExtension(mjpegCfrPath, ".timing.csv");
        cameraService.WriteTimingAuditSidecar(mjpegCfrCsv);

        var probeMjpegCfr = ProbeMp4Stream(mjpegCfrPath);
        LogStreamAudit("1080p MJPEG CFR (Original)", mjpegCfrPath, mjpegCfrAudit, probeMjpegCfr, RecordingCadenceMode.CFR_Compatibility, Log);

        await cameraService.StopCaptureAsync().ConfigureAwait(false);

        // =========================================================================
        // TEST 3 & 4: 1080p YUY2 (5 FPS Bandwidth Limit)
        // =========================================================================
        if (yuy21080 != null)
        {
            Log("\n==================================================================");
            Log("  TEST 3: 1080p YUY2 Capture-Faithful VFR (5 FPS Bandwidth Limit)");
            Log("==================================================================");

            await cameraService.StartCaptureAsync(camera, yuy21080).ConfigureAwait(false);
            Log("  Waiting 1.5s for 5 FPS stream initialization...");
            await Task.Delay(1500).ConfigureAwait(false);

            string yuy2VfrPath = Path.Combine(outputDir, $"WebcamLab_Phase13_2_YUY2_VFR_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            await cameraService.StartRecordingAsync(yuy2VfrPath, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR).ConfigureAwait(false);
            await Task.Delay(3500).ConfigureAwait(false);
            await cameraService.StopRecordingAsync().ConfigureAwait(false);

            var yuy2VfrAudit = cameraService.GetLastRecordingAuditRecords();
            string yuy2VfrCsv = Path.ChangeExtension(yuy2VfrPath, ".timing.csv");
            cameraService.WriteTimingAuditSidecar(yuy2VfrCsv);

            var probeYuy2Vfr = ProbeMp4Stream(yuy2VfrPath);
            LogStreamAudit("1080p YUY2 VFR (Original)", yuy2VfrPath, yuy2VfrAudit, probeYuy2Vfr, RecordingCadenceMode.Capture_Faithful_VFR, Log);

            Log("\n==================================================================");
            Log("  TEST 4: 1080p YUY2 CFR Compatibility Mode (5 FPS Target Rate)");
            Log("==================================================================");

            string yuy2CfrPath = Path.Combine(outputDir, $"WebcamLab_Phase13_2_YUY2_CFR_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            await cameraService.StartRecordingAsync(yuy2CfrPath, RecordingMode.OriginalCamera, RecordingCadenceMode.CFR_Compatibility).ConfigureAwait(false);
            await Task.Delay(3500).ConfigureAwait(false);
            await cameraService.StopRecordingAsync().ConfigureAwait(false);

            var yuy2CfrAudit = cameraService.GetLastRecordingAuditRecords();
            string yuy2CfrCsv = Path.ChangeExtension(yuy2CfrPath, ".timing.csv");
            cameraService.WriteTimingAuditSidecar(yuy2CfrCsv);

            var probeYuy2Cfr = ProbeMp4Stream(yuy2CfrPath);
            LogStreamAudit("1080p YUY2 CFR (Original)", yuy2CfrPath, yuy2CfrAudit, probeYuy2Cfr, RecordingCadenceMode.CFR_Compatibility, Log);

            await cameraService.StopCaptureAsync().ConfigureAwait(false);
        }

        // =========================================================================
        // TEST 5: Graded Video Recording (Film Warmth Look, VFR Mode)
        // =========================================================================
        Log("\n==================================================================");
        Log("  TEST 5: Graded Video Recording in Capture-Faithful VFR Mode");
        Log("==================================================================");

        await cameraService.StartCaptureAsync(camera, mjpeg1080).ConfigureAwait(false);
        Log("  Waiting 1.5s for camera auto-exposure settlement...");
        await Task.Delay(1500).ConfigureAwait(false);

        // Configure Film Warmth Look (Warm 5600K->3200K aesthetic)
        cameraService.LookSettings = new SoftwareLookSettings
        {
            Enabled = true,
            LookName = "Film Warmth",
            Saturation = 1.15,
            Contrast = 1.10,
            Highlights = -5.0,
            Shadows = 10.0,
            SCurveStrength = 0.35
        };
        cameraService.IsGradedPreviewActive = true;

        string gradedVfrPath = Path.Combine(outputDir, $"WebcamLab_Phase13_2_Graded_VFR_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        await cameraService.StartRecordingAsync(gradedVfrPath, RecordingMode.GradedVideo, RecordingCadenceMode.Capture_Faithful_VFR).ConfigureAwait(false);
        await Task.Delay(3500).ConfigureAwait(false);
        await cameraService.StopRecordingAsync().ConfigureAwait(false);

        var gradedVfrAudit = cameraService.GetLastRecordingAuditRecords();
        string gradedVfrCsv = Path.ChangeExtension(gradedVfrPath, ".timing.csv");
        cameraService.WriteTimingAuditSidecar(gradedVfrCsv);

        var probeGradedVfr = ProbeMp4Stream(gradedVfrPath);
        LogStreamAudit("1080p Graded VFR (Film Warmth)", gradedVfrPath, gradedVfrAudit, probeGradedVfr, RecordingCadenceMode.Capture_Faithful_VFR, Log);

        await cameraService.StopCaptureAsync().ConfigureAwait(false);

        // Write complete investigation log
        string reportPath = Path.Combine(AppContext.BaseDirectory, "Dell_UZ2315H_Phase13_2_Report.txt");
        try
        {
            File.WriteAllText(reportPath, sb.ToString());
            string artifactDir = @"C:\Users\sambu\.gemini\antigravity\brain\b590e0c0-8f95-4445-a4b2-bb3470372bae";
            if (Directory.Exists(artifactDir))
            {
                File.WriteAllText(Path.Combine(artifactDir, "Dell_UZ2315H_Phase13_2_Report.txt"), sb.ToString());
            }
        }
        catch { }

        Log("\n==================================================================");
        Log($"  Phase 13.2 Report saved to: {reportPath}");
        Log("==================================================================");

        return true;
    }

    private static void LogStreamAudit(string title, string filePath, IReadOnlyList<FrameTimingAuditRecord> auditRecords, StreamProbeResult probe, RecordingCadenceMode cadence, Action<string> Log)
    {
        Log($"\n  --- {title} ---");
        int paddedDuplicates = Math.Max(0, probe.FrameCount - auditRecords.Count);
        Log($"  File Path:              {filePath} ({new FileInfo(filePath).Length:N0} bytes)");
        Log($"  Source Samples:         {auditRecords.Count} captured");
        Log($"  Decoded MP4 Frames:     {probe.FrameCount} frames (FFprobe)");
        Log($"  Padded Duplicates:      {paddedDuplicates} frames");

        double sourceSpanSec = auditRecords.Count > 1 ? auditRecords[^1].NormalizedTimeSec : 0.0;
        double durationErrorMs = Math.Abs(probe.DurationSeconds - sourceSpanSec) * 1000.0;

        Log($"  Stream Duration:        {probe.DurationSeconds:F3}s (Decoded), Source Span: {sourceSpanSec:F3}s (Error: {durationErrorMs:F2} ms)");
        Log($"  Average Frame Rate:     {probe.AverageFps:F2} FPS");
        Log($"  Frame Intervals:        Min={probe.MinDeltaMs:F1}ms, Max={probe.MaxDeltaMs:F1}ms, Mean={probe.MeanDeltaMs:F1}ms");

        // Calculate timestamp deviations between source sample time and MP4 decoded PTS
        int compareCount = Math.Min(auditRecords.Count, probe.PtsTimes.Count);
        var deviations = new List<double>();
        int monotonicClamps = 0;

        for (int i = 0; i < compareCount; i++)
        {
            var audit = auditRecords[i];
            if (audit.MonotonicityCorrected) monotonicClamps++;

            double sourceSec = audit.NormalizedTimeSec;
            double mp4Sec = probe.PtsTimes[i];
            double devMs = Math.Abs(mp4Sec - sourceSec) * 1000.0;
            deviations.Add(devMs);
        }

        deviations.Sort();
        double sumDeviationMs = 0.0;
        foreach (var d in deviations) sumDeviationMs += d;

        double meanDeviationMs = compareCount > 0 ? sumDeviationMs / compareCount : 0.0;
        double medianDeviationMs = 0.0;
        double p95DeviationMs = 0.0;
        double maxDeviationMs = 0.0;

        if (deviations.Count > 0)
        {
            medianDeviationMs = deviations[deviations.Count / 2];
            int p95Index = (int)Math.Min(deviations.Count - 1, Math.Round(deviations.Count * 0.95));
            p95DeviationMs = deviations[p95Index];
            maxDeviationMs = deviations[^1];
        }

        Log($"  Monotonicity Clamps:    {monotonicClamps} / {auditRecords.Count} samples");
        Log($"  Source-to-MP4 PTS MAE:  {meanDeviationMs:F3} ms (Mean Absolute Error)");
        Log($"  Median Delta:           {medianDeviationMs:F3} ms");
        Log($"  95th Percentile Delta:  {p95DeviationMs:F3} ms");
        Log($"  Max Timestamp Delta:    {maxDeviationMs:F3} ms");

        // Print side-by-side comparison of first 8 frames
        Log("  Sample Timing Comparison (First 8 Frames):");
        Log("  Idx | Source HNS      | Raw Sec  | Norm Sec | MP4 PTS  | Delta (ms)");
        Log("  ----+-----------------+----------+----------+----------+-----------");
        int printCount = Math.Min(8, compareCount);
        for (int i = 0; i < printCount; i++)
        {
            var a = auditRecords[i];
            double pts = probe.PtsTimes[i];
            double deltaMs = (pts - a.NormalizedTimeSec) * 1000.0;
            Log($"  {a.FrameIndex,3} | {a.OriginalSourceTimestampHns,15} | {a.RawSourceTimeSec,8:F4} | {a.NormalizedTimeSec,8:F4} | {pts,8:F4} | {deltaMs,9:F3} ms");
        }
    }

    private static StreamProbeResult ProbeMp4Stream(string mp4Path)
    {
        var result = new StreamProbeResult();
        string ffprobePath = FindFFprobe();
        if (string.IsNullOrEmpty(ffprobePath) || !File.Exists(mp4Path)) return result;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -select_streams v:0 -show_entries frame=pts_time,duration_time -of csv=p=0 \"{mp4Path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return result;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double pts))
                {
                    result.PtsTimes.Add(pts);
                    result.FrameCount++;
                }
            }

            if (result.PtsTimes.Count > 1)
            {
                double minDelta = double.MaxValue;
                double maxDelta = 0.0;
                double totalDelta = 0.0;
                int deltaCount = 0;

                for (int i = 1; i < result.PtsTimes.Count; i++)
                {
                    double delta = (result.PtsTimes[i] - result.PtsTimes[i - 1]) * 1000.0;
                    if (delta < minDelta) minDelta = delta;
                    if (delta > maxDelta) maxDelta = delta;
                    totalDelta += delta;
                    deltaCount++;
                }

                result.MinDeltaMs = minDelta;
                result.MaxDeltaMs = maxDelta;
                result.MeanDeltaMs = deltaCount > 0 ? totalDelta / deltaCount : 0.0;
                result.DurationSeconds = result.PtsTimes[^1] + (result.MeanDeltaMs / 1000.0);
                result.AverageFps = result.DurationSeconds > 0 ? result.FrameCount / result.DurationSeconds : 0.0;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error probing MP4 stream: {ex.Message}");
        }

        return result;
    }

    private static string FindFFprobe()
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
        foreach (var p in paths)
        {
            string candidate = Path.Combine(p.Trim(), "ffprobe.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return "ffprobe";
    }
}
