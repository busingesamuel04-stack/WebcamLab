using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebcamLab.Capture;
using WebcamLab.Core.Models;

namespace WebcamLab.Core.Validation;

public static class Phase23LiveBurstRunner
{
    public static async Task<bool> RunLiveBurstExperimentsAsync(string outputDir)
    {
        var sb = new StringBuilder();
        void Log(string msg)
        {
            Console.WriteLine(msg);
            sb.AppendLine(msg);
        }

        Log("\n==================================================================");
        Log("  WebcamLab Phase 23: Live Multi-Frame Still Enhancement Experiment");
        Log("==================================================================");

        Directory.CreateDirectory(outputDir);
        using var cameraService = new MediaFoundationCameraService();

        // 1. Discover camera
        Log("\n[1/6] Discovering physical camera devices...");
        var cameras = await cameraService.EnumerateCamerasAsync().ConfigureAwait(false);
        var targetCamera = cameras.FirstOrDefault(c => c.IsDellUZ2315H) ?? cameras.FirstOrDefault();
        if (targetCamera == null)
        {
            Log("      [FAIL] No camera devices found.");
            SaveLog(outputDir, sb);
            return false;
        }
        Log($"      [PASS] Selected camera: {targetCamera.Name}");

        var formats = await cameraService.GetSupportedFormatsAsync(targetCamera).ConfigureAwait(false);
        var yuy2Format = formats.FirstOrDefault(f => f.FourCC == "YUY2" && f.Width == 1920 && f.Height == 1080);
        var mjpgFormat = formats.FirstOrDefault(f => f.FourCC == "MJPG" && f.Width == 1920 && f.Height == 1080);

        if (yuy2Format == null || mjpgFormat == null)
        {
            Log("      [FAIL] Missing required 1080p YUY2 or MJPEG stream formats.");
            SaveLog(outputDir, sb);
            return false;
        }

        // 2. Start YUY2 1080p stream
        Log("\n[2/6] Starting 1080p YUY2 stream (Uncompressed 4:2:2 @ 5 FPS)...");
        await cameraService.StartCaptureAsync(targetCamera, yuy2Format).ConfigureAwait(false);
        Log("      Waiting 2.5s for sensor AE/AWB convergence...");
        await Task.Delay(2500).ConfigureAwait(false);

        // Baseline single snapshot in YUY2
        Log("\n[3/6] Capturing baseline single snapshot in YUY2 1080p...");
        string singleYuy2Path = await cameraService.CaptureStillImageAsync(outputDir, "png").ConfigureAwait(false);
        var singleFi = new FileInfo(singleYuy2Path);
        Log($"      [PASS] Single still saved: {Path.GetFileName(singleYuy2Path)} ({singleFi.Length:N0} bytes)");

        // Multi-frame bursts in YUY2: 4, 8, 12 frames
        Log("\n[4/6] Executing multi-frame bursts in YUY2 1080p...");

        // 4a. 4 frames
        Log("      --> Burst A: 4 frames (YUY2 1080p)...");
        var settings4 = new MultiFrameBurstSettings { BurstSize = 4 };
        var sidecar4 = await cameraService.CaptureEnhancedStillAsync(outputDir, settings4).ConfigureAwait(false);
        Log($"          Fused: {sidecar4.FramesAcceptedForFusion}/{sidecar4.BurstSizeRequested} frames in {sidecar4.TotalBurstCaptureDurationMs:F0} ms (Proc: {sidecar4.ProcessingDurationMs:F1} ms)");
        Log($"          Noise Reduction: +{sidecar4.MeasuredNoiseReductionDb:F2} dB | Edge Retention: {sidecar4.MeasuredEdgeRetentionRatio * 100:F1}% | Motion Mask: {sidecar4.MotionMaskPixelPercentage:F1}%");
        Log($"          Original: {Path.GetFileName(sidecar4.OriginalImagePath)}");
        Log($"          Enhanced: {Path.GetFileName(sidecar4.EnhancedImagePath)}");
        Log($"          Sidecar:  {Path.GetFileName(sidecar4.SidecarPath)}");

        await Task.Delay(1000).ConfigureAwait(false);

        // 4b. 8 frames
        Log("      --> Burst B: 8 frames (YUY2 1080p)...");
        var settings8 = new MultiFrameBurstSettings { BurstSize = 8 };
        var sidecar8 = await cameraService.CaptureEnhancedStillAsync(outputDir, settings8).ConfigureAwait(false);
        Log($"          Fused: {sidecar8.FramesAcceptedForFusion}/{sidecar8.BurstSizeRequested} frames in {sidecar8.TotalBurstCaptureDurationMs:F0} ms (Proc: {sidecar8.ProcessingDurationMs:F1} ms)");
        Log($"          Noise Reduction: +{sidecar8.MeasuredNoiseReductionDb:F2} dB | Edge Retention: {sidecar8.MeasuredEdgeRetentionRatio * 100:F1}% | Motion Mask: {sidecar8.MotionMaskPixelPercentage:F1}%");
        Log($"          Original: {Path.GetFileName(sidecar8.OriginalImagePath)}");
        Log($"          Enhanced: {Path.GetFileName(sidecar8.EnhancedImagePath)}");
        Log($"          Sidecar:  {Path.GetFileName(sidecar8.SidecarPath)}");

        await Task.Delay(1000).ConfigureAwait(false);

        // 4c. 12 frames
        Log("      --> Burst C: 12 frames (YUY2 1080p)...");
        var settings12 = new MultiFrameBurstSettings { BurstSize = 12 };
        var sidecar12 = await cameraService.CaptureEnhancedStillAsync(outputDir, settings12).ConfigureAwait(false);
        Log($"          Fused: {sidecar12.FramesAcceptedForFusion}/{sidecar12.BurstSizeRequested} frames in {sidecar12.TotalBurstCaptureDurationMs:F0} ms (Proc: {sidecar12.ProcessingDurationMs:F1} ms)");
        Log($"          Noise Reduction: +{sidecar12.MeasuredNoiseReductionDb:F2} dB | Edge Retention: {sidecar12.MeasuredEdgeRetentionRatio * 100:F1}% | Motion Mask: {sidecar12.MotionMaskPixelPercentage:F1}%");
        Log($"          Original: {Path.GetFileName(sidecar12.OriginalImagePath)}");
        Log($"          Enhanced: {Path.GetFileName(sidecar12.EnhancedImagePath)}");
        Log($"          Sidecar:  {Path.GetFileName(sidecar12.SidecarPath)}");

        // 5. Switch to MJPEG 1080p stream
        Log("\n[5/6] Switching to 1080p MJPEG stream (@ 30 FPS nominal)...");
        await cameraService.StopCaptureAsync().ConfigureAwait(false);
        await Task.Delay(500).ConfigureAwait(false);
        await cameraService.StartCaptureAsync(targetCamera, mjpgFormat).ConfigureAwait(false);
        Log("      Waiting 2.0s for MJPEG stream stabilization...");
        await Task.Delay(2000).ConfigureAwait(false);

        // Baseline MJPEG snapshot
        string singleMjpgPath = await cameraService.CaptureStillImageAsync(outputDir, "jpg").ConfigureAwait(false);
        var singleMjpgFi = new FileInfo(singleMjpgPath);
        Log($"      [PASS] Single MJPEG still saved: {Path.GetFileName(singleMjpgPath)} ({singleMjpgFi.Length:N0} bytes)");

        // 8-frame burst in MJPEG
        Log("      --> Burst D: 8 frames (MJPEG 1080p)...");
        var settingsMjpg8 = new MultiFrameBurstSettings { BurstSize = 8 };
        var sidecarMjpg8 = await cameraService.CaptureEnhancedStillAsync(outputDir, settingsMjpg8).ConfigureAwait(false);
        Log($"          Fused: {sidecarMjpg8.FramesAcceptedForFusion}/{sidecarMjpg8.BurstSizeRequested} frames in {sidecarMjpg8.TotalBurstCaptureDurationMs:F0} ms (Proc: {sidecarMjpg8.ProcessingDurationMs:F1} ms)");
        Log($"          Noise Reduction: +{sidecarMjpg8.MeasuredNoiseReductionDb:F2} dB | Edge Retention: {sidecarMjpg8.MeasuredEdgeRetentionRatio * 100:F1}% | Motion Mask: {sidecarMjpg8.MotionMaskPixelPercentage:F1}%");
        Log($"          Original: {Path.GetFileName(sidecarMjpg8.OriginalImagePath)}");
        Log($"          Enhanced: {Path.GetFileName(sidecarMjpg8.EnhancedImagePath)}");
        Log($"          Sidecar:  {Path.GetFileName(sidecarMjpg8.SidecarPath)}");

        // 6. Cleanup
        Log("\n[6/6] Stopping capture stream and finalizing session...");
        await cameraService.StopCaptureAsync().ConfigureAwait(false);

        bool allBurstsValid = sidecar4.FramesAcceptedForFusion >= 3 &&
                              sidecar8.FramesAcceptedForFusion >= 5 &&
                              sidecar12.FramesAcceptedForFusion >= 8 &&
                              sidecarMjpg8.FramesAcceptedForFusion >= 5 &&
                              File.Exists(sidecar4.EnhancedImagePath) &&
                              File.Exists(sidecar8.EnhancedImagePath) &&
                              File.Exists(sidecar12.EnhancedImagePath) &&
                              File.Exists(sidecarMjpg8.EnhancedImagePath);

        Log("------------------------------------------------------------------");
        if (allBurstsValid)
        {
            Log("  ALL PHYSICAL MULTI-FRAME BURST EXPERIMENTS PASSED CLEANLY!");
        }
        else
        {
            Log("  MULTI-FRAME BURST EXPERIMENTS COMPLETED WITH WARNINGS.");
        }
        Log("==================================================================");

        SaveLog(outputDir, sb);
        return allBurstsValid;
    }

    private static void SaveLog(string outputDir, StringBuilder sb)
    {
        try
        {
            string path = Path.Combine(outputDir, $"Phase23_LiveBurst_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"Live burst report written to: {path}");
        }
        catch { }
    }
}
