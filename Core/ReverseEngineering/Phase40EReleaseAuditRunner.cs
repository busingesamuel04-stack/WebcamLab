using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebcamLab.Capture;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Models;
using WebcamLab.Core.Native;

namespace WebcamLab.Core.ReverseEngineering;

public class Phase40ETestResult
{
    public string ScenarioId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string Mp4Path { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public bool HasMoovAtom { get; set; }
    public double VideoDurationSeconds { get; set; }
    public double AudioDurationSeconds { get; set; }
    public double TrackDeltaMs { get; set; }
    public bool CleanVideoDecode { get; set; }
    public bool CleanAudioDecode { get; set; }
    public long FinalizationElapsedMs { get; set; }
    public int FramesCaptured { get; set; }
    public int FramesEncoded { get; set; }
    public int FramesDropped { get; set; }

    // Physical Clapper / Hardware A/V Synchronization
    public double? InitialHardwareOffsetMs { get; set; }
    public double? FinalHardwareOffsetMs { get; set; }
    public double? HardwareClockDriftMs { get; set; }
    public double? DriftRateMsPerMin { get; set; }
    public double? ClockPpm { get; set; }
    public string Notes { get; set; } = "";
}

public static class Phase40EReleaseAuditRunner
{
    public static readonly string OutputDir = @"E:\WebcamLab_Phase40E_Audit";

    public static async Task<bool> RunAllAsync()
    {
        Directory.CreateDirectory(OutputDir);

        Console.WriteLine("\n==================================================================");
        Console.WriteLine("  WebcamLab Phase 40E: Final RC4 Release Audit");
        Console.WriteLine("  Hardware Target: Dell UZ2315H (Realtek RTS5875 + Integrated Audio)");
        Console.WriteLine($"  Output Directory: {OutputDir}");
        Console.WriteLine("==================================================================");

        using var cameraService = new MediaFoundationCameraService();
        var cameras = await cameraService.EnumerateCamerasAsync();
        var camera = cameras.FirstOrDefault(c => c.IsDellUZ2315H) ?? cameras.FirstOrDefault();

        if (camera == null)
        {
            Console.WriteLine("ERROR: No physical webcam discovered!");
            return false;
        }

        var formats = await cameraService.GetSupportedFormatsAsync(camera);
        var mjpg1080 = formats.FirstOrDefault(f => f.FourCC == "MJPG" && f.Width == 1920 && f.Height == 1080)
                       ?? formats.FirstOrDefault();

        if (mjpg1080 == null)
        {
            Console.WriteLine("ERROR: Could not find 1080p MJPEG format!");
            return false;
        }

        var audioDevs = await AudioDeviceService.EnumerateCaptureDevicesAsync();
        var audioDevice = audioDevs.FirstOrDefault(a => a.IsDellUZ2315H)
                          ?? audioDevs.FirstOrDefault(a => a.IsDefault)
                          ?? audioDevs.FirstOrDefault();

        if (audioDevice == null)
        {
            Console.WriteLine("ERROR: No suitable audio device discovered!");
            return false;
        }

        Console.WriteLine($"Camera: {camera.Name} [{camera.SymbolicLink}]");
        Console.WriteLine($"Audio:  {audioDevice.FriendlyName} [{audioDevice.FormatSummary}]\n");

        var results = new List<Phase40ETestResult>();
        bool allPassed = true;

        // 1. Fresh Normal Recording 1 (15s)
        Console.WriteLine(">>> Running Fresh Normal Recording 1 (15s)...");
        var res1 = await RunNormalRecordingAsync(cameraService, camera, mjpg1080, audioDevice, "RUN1_15S", "Fresh Normal Recording 1 (15s)", 15000);
        results.Add(res1);
        if (!res1.Passed) allPassed = false;

        // 2. Fresh Normal Recording 2 (20s)
        Console.WriteLine("\n>>> Running Fresh Normal Recording 2 (20s)...");
        var res2 = await RunNormalRecordingAsync(cameraService, camera, mjpg1080, audioDevice, "RUN2_20S", "Fresh Normal Recording 2 (20s)", 20000);
        results.Add(res2);
        if (!res2.Passed) allPassed = false;

        // 3. Fresh Normal Recording 3 (30s)
        Console.WriteLine("\n>>> Running Fresh Normal Recording 3 (30s)...");
        var res3 = await RunNormalRecordingAsync(cameraService, camera, mjpg1080, audioDevice, "RUN3_30S", "Fresh Normal Recording 3 (30s)", 30000);
        results.Add(res3);
        if (!res3.Passed) allPassed = false;

        // 4. Delayed-Audio-Start Calibration Test (2.8s simulated USB startup delay)
        Console.WriteLine("\n>>> Running Delayed-Audio-Start Verification (2.8s delay)...");
        var res4 = await RunDelayedAudioStartTestAsync(cameraService, camera, mjpg1080, audioDevice);
        results.Add(res4);
        if (!res4.Passed) allPassed = false;

        // Write summary report
        WriteSummaryReport(results);

        Console.WriteLine("\n==================================================================");
        Console.WriteLine(allPassed ? "  ALL PHASE 40E AUDIT SCENARIOS PASSED CLEANLY!" : "  PHASE 40E AUDIT COMPLETED WITH FAILURES");
        Console.WriteLine("==================================================================");

        return allPassed;
    }

    private static async Task<Phase40ETestResult> RunNormalRecordingAsync(
        MediaFoundationCameraService cameraService,
        CameraDevice camera,
        CameraFormat format,
        AudioDeviceInfo audioDevice,
        string id,
        string name,
        int durationMs)
    {
        string mp4Path = Path.Combine(OutputDir, $"Phase40E_{id}.mp4");

        await cameraService.StartCaptureAsync(camera, format);
        await Task.Delay(1500); // 1.5s preview spin-up

        await cameraService.StartRecordingAsync(mp4Path, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);
        Console.WriteLine($"  [{id}] Recording active for {durationMs / 1000.0:F1}s...");
        await Task.Delay(durationMs);

        var sw = Stopwatch.StartNew();
        await cameraService.StopRecordingAsync();
        sw.Stop();
        Console.WriteLine($"  [{id}] Stop completed in {sw.ElapsedMilliseconds} ms.");

        await cameraService.StopCaptureAsync();
        await Task.Delay(500);

        return AuditRecording(id, name, mp4Path, sw.ElapsedMilliseconds, cameraService);
    }

    private static async Task<Phase40ETestResult> RunDelayedAudioStartTestAsync(
        MediaFoundationCameraService cameraService,
        CameraDevice camera,
        CameraFormat format,
        AudioDeviceInfo audioDevice)
    {
        string id = "DELAYED_AUDIO";
        string name = "Delayed-Audio-Start Test (2.8s Delayed Spinup)";
        string mp4Path = Path.Combine(OutputDir, $"Phase40E_{id}.mp4");

        await cameraService.StartCaptureAsync(camera, format);
        await Task.Delay(1500);

        // Start recording with audio enabled
        await cameraService.StartRecordingAsync(mp4Path, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);

        // Intentionally suspend audio capture immediately to simulate 2.8s cold USB audio initialization
        Console.WriteLine("  [DELAYED_AUDIO] Suspending audio capture for 2.8s to simulate cold USB audio spinup...");
        cameraService.AudioCapture.Stop();
        await Task.Delay(2800);

        // Re-start audio capture mid-recording
        Console.WriteLine("  [DELAYED_AUDIO] Delayed audio arriving now (starting WASAPI capture)...");
        cameraService.AudioCapture.Start(audioDevice.Id);

        // Let recording continue with active audio for 10 seconds
        Console.WriteLine("  [DELAYED_AUDIO] Recording for 10.0s with active audio...");
        await Task.Delay(10000);

        var sw = Stopwatch.StartNew();
        await cameraService.StopRecordingAsync();
        sw.Stop();
        Console.WriteLine($"  [DELAYED_AUDIO] Stop completed in {sw.ElapsedMilliseconds} ms.");

        await cameraService.StopCaptureAsync();
        await Task.Delay(500);

        var res = AuditRecording(id, name, mp4Path, sw.ElapsedMilliseconds, cameraService);
        res.Notes = "Simulated 2.8s cold USB audio initialization";
        return res;
    }

    private static Phase40ETestResult AuditRecording(
        string id,
        string name,
        string mp4Path,
        long stopMs,
        MediaFoundationCameraService cameraService)
    {
        var res = new Phase40ETestResult
        {
            ScenarioId = id,
            Name = name,
            Mp4Path = mp4Path,
            FinalizationElapsedMs = stopMs,
            FileSizeBytes = File.Exists(mp4Path) ? new FileInfo(mp4Path).Length : 0,
            HasMoovAtom = CheckMoovAtom(mp4Path)
        };

        // Probe streams via ffprobe
        ProbeMediaFile(mp4Path, res);

        // Verify full decode via ffmpeg
        res.CleanVideoDecode = VerifyDecode(mp4Path, isVideo: true);
        res.CleanAudioDecode = VerifyDecode(mp4Path, isVideo: false);

        res.TrackDeltaMs = Math.Abs(res.VideoDurationSeconds - res.AudioDurationSeconds) * 1000.0;

        // Telemetry
        var tele = cameraService.GetRecordingTelemetry();
        res.FramesCaptured = tele.FramesCaptured;
        res.FramesEncoded = tele.FramesEncoded;
        res.FramesDropped = tele.DroppedFrames;

        var syncReport = cameraService.ActiveRecorder.LastSyncReport;
        if (syncReport != null)
        {
            res.InitialHardwareOffsetMs = syncReport.InitialOffsetMs;
            res.FinalHardwareOffsetMs = syncReport.FinalOffsetMs;
            res.HardwareClockDriftMs = syncReport.DurationDriftMs;
            res.DriftRateMsPerMin = syncReport.DriftRateMsPerMin;
            res.ClockPpm = syncReport.ClockPpm;
        }

        // Evaluation criteria:
        // 1. File exists and size > 50 KB
        // 2. Has valid moov atom
        // 3. Clean video decode (0 errors)
        // 4. Clean audio decode (0 errors)
        // 5. Track-duration delta <= 35.0 ms
        // 6. Stop elapsed < 3000 ms
        res.Passed = res.FileSizeBytes > 50_000 &&
                     res.HasMoovAtom &&
                     res.CleanVideoDecode &&
                     res.CleanAudioDecode &&
                     res.TrackDeltaMs <= 35.0 &&
                     res.FinalizationElapsedMs < 3000;

        Console.WriteLine($"    [Audit {id}] Size: {res.FileSizeBytes:N0} bytes | Moov: {res.HasMoovAtom} | Video: {res.VideoDurationSeconds:F3}s ({res.FramesCaptured} frames) | Audio: {res.AudioDurationSeconds:F3}s | Track Delta: {res.TrackDeltaMs:F2} ms | Clapper Offset: {res.InitialHardwareOffsetMs:F1} ms | Stop: {res.FinalizationElapsedMs} ms | Result: {(res.Passed ? "PASS" : "FAIL")}");

        return res;
    }

    private static bool CheckMoovAtom(string mp4Path)
    {
        try
        {
            using var fs = File.OpenRead(mp4Path);
            int scanLen = (int)Math.Min(fs.Length, 2 * 1024 * 1024);
            fs.Seek(Math.Max(0, fs.Length - scanLen), SeekOrigin.Begin);
            byte[] buf = new byte[scanLen];
            int read = fs.Read(buf, 0, buf.Length);
            byte[] pat = Encoding.ASCII.GetBytes("moov");
            for (int i = 0; i < read - 4; i++)
            {
                if (buf[i] == pat[0] && buf[i + 1] == pat[1] && buf[i + 2] == pat[2] && buf[i + 3] == pat[3])
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static void ProbeMediaFile(string mp4Path, Phase40ETestResult res)
    {
        try
        {
            var psi = new ProcessStartInfo("ffprobe", $"-v error -show_entries stream=codec_name,codec_type,duration,nb_frames -of json \"{mp4Path}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                string json = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("streams", out var streams))
                {
                    foreach (var s in streams.EnumerateArray())
                    {
                        string type = s.GetProperty("codec_type").GetString() ?? "";
                        if (type == "video" && s.TryGetProperty("duration", out var vd))
                        {
                            if (double.TryParse(vd.GetString(), out double vSec)) res.VideoDurationSeconds = vSec;
                        }
                        else if (type == "audio" && s.TryGetProperty("duration", out var ad))
                        {
                            if (double.TryParse(ad.GetString(), out double aSec)) res.AudioDurationSeconds = aSec;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static bool VerifyDecode(string mp4Path, bool isVideo)
    {
        try
        {
            string map = isVideo ? "-map 0:v -f rawvideo" : "-map 0:a -f s16le";
            var psi = new ProcessStartInfo("ffmpeg", $"-v error -i \"{mp4Path}\" {map} -y NUL")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 && string.IsNullOrWhiteSpace(err);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteSummaryReport(List<Phase40ETestResult> results)
    {
        string reportPath = Path.Combine(OutputDir, "Phase40E_Summary_Report.txt");
        var sb = new StringBuilder();
        sb.AppendLine("==================================================================");
        sb.AppendLine("  WebcamLab Phase 40E Release Audit Summary Report");
        sb.AppendLine($"  Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("==================================================================");
        sb.AppendLine();

        foreach (var r in results)
        {
            sb.AppendLine($"Scenario: {r.ScenarioId} — {r.Name}");
            sb.AppendLine($"  Result:                  {(r.Passed ? "PASS" : "FAIL")}");
            sb.AppendLine($"  File:                    {r.Mp4Path}");
            sb.AppendLine($"  Size:                    {r.FileSizeBytes:N0} bytes");
            sb.AppendLine($"  Moov Atom:               {r.HasMoovAtom}");
            sb.AppendLine($"  Video Dur:               {r.VideoDurationSeconds:F3} s ({r.FramesCaptured} captured, {r.FramesDropped} dropped)");
            sb.AppendLine($"  Audio Dur:               {r.AudioDurationSeconds:F3} s");
            sb.AppendLine($"  Track-Duration Delta:    {r.TrackDeltaMs:F2} ms (Target <= 35.0 ms)");
            sb.AppendLine($"  Physical Clapper Offset: {r.InitialHardwareOffsetMs:F2} ms");
            sb.AppendLine($"  Physical Clock Drift:    {r.HardwareClockDriftMs:F2} ms ({r.DriftRateMsPerMin:F2} ms/min, {r.ClockPpm:F1} ppm)");
            sb.AppendLine($"  Decode Clean:            Video={r.CleanVideoDecode}, Audio={r.CleanAudioDecode}");
            sb.AppendLine($"  Stop Latency:            {r.FinalizationElapsedMs} ms");
            if (!string.IsNullOrEmpty(r.Notes)) sb.AppendLine($"  Notes:                   {r.Notes}");
            sb.AppendLine();
        }

        File.WriteAllText(reportPath, sb.ToString());
        Console.WriteLine($"\nSummary report saved to: {reportPath}");
    }
}
