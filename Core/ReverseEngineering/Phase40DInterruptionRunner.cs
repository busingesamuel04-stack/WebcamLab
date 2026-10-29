using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebcamLab.Capture;
using WebcamLab.Capture.Audio;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Models;
using WebcamLab.Core.Validation;

namespace WebcamLab.Core.ReverseEngineering;

public class Phase40DTestScenarioResult
{
    public string ScenarioId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Mp4Path { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public double VideoDurationSeconds { get; set; }
    public double AudioDurationSeconds { get; set; }
    public int FramesCaptured { get; set; }
    public int FramesEncoded { get; set; }
    public int FramesDropped { get; set; }
    public double TrackDeltaMs { get; set; }
    public bool HasMoovAtom { get; set; }
    public bool CleanVideoDecode { get; set; }
    public bool CleanAudioDecode { get; set; }
    public long FinalizationElapsedMs { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public static class Phase40DInterruptionRunner
{
    public static string OutputDir => @"E:\WebcamLab_Phase40D_Interruption";

    public static async Task<bool> RunAllAsync(string filter = "ALL")
    {
        Console.WriteLine();
        Console.WriteLine("==================================================================");
        Console.WriteLine("  WebcamLab Phase 40D: Edge-Case & Interruption Reliability Testing");
        Console.WriteLine("  Hardware Target: Dell UZ2315H (Realtek RTS5875 + Integrated Audio)");
        Console.WriteLine($"  Output Directory: {OutputDir}");
        Console.WriteLine("==================================================================");

        if (!Directory.Exists(OutputDir))
        {
            Directory.CreateDirectory(OutputDir);
        }

        var results = new List<Phase40DTestScenarioResult>();
        bool allPassed = true;

        using var cameraService = new MediaFoundationCameraService();

        // 1. Discover Camera
        var cameras = await cameraService.EnumerateCamerasAsync();
        var camera = cameras.FirstOrDefault(c => c.IsDellUZ2315H) ?? cameras.FirstOrDefault();
        if (camera == null)
        {
            Console.WriteLine("[FAIL] Dell UZ2315H Camera not detected.");
            return false;
        }

        // 2. Discover Audio Endpoint
        var audioDevs = await AudioDeviceService.EnumerateCaptureDevicesAsync();
        var audioDevice = audioDevs.FirstOrDefault(a => a.IsDellUZ2315H) ?? audioDevs.FirstOrDefault(a => a.IsDefault) ?? audioDevs.FirstOrDefault();
        if (audioDevice == null)
        {
            Console.WriteLine("[FAIL] Audio endpoint not detected.");
            return false;
        }

        Console.WriteLine($"Camera: {camera.Name} [{camera.SymbolicLink}]");
        Console.WriteLine($"Audio:  {audioDevice.FriendlyName} [{audioDevice.FormatSummary}]\n");

        var formats = await cameraService.GetSupportedFormatsAsync(camera);
        var mjpg1080 = formats.FirstOrDefault(f => f.FourCC == "MJPG" && f.Width == 1920 && f.Height == 1080);
        if (mjpg1080 == null)
        {
            Console.WriteLine("[FAIL] 1080p MJPEG stream format not found.");
            return false;
        }

        // Test A: Normal stop and immediate restart
        if (filter == "ALL" || filter == "A")
        {
            Console.WriteLine("\n>>> Running Test A: Normal stop and immediate restart (35s back-to-back)...");
            var resA = await RunTestAAsync(cameraService, camera, mjpg1080, audioDevice);
            results.AddRange(resA);
            if (resA.Any(r => !r.Passed)) allPassed = false;
        }

        // Test B: Camera disconnection / capture interruption during recording
        if (filter == "ALL" || filter == "B")
        {
            Console.WriteLine("\n>>> Running Test B: Camera capture stream interruption during active recording...");
            var resB = await RunTestBAsync(cameraService, camera, mjpg1080, audioDevice);
            results.Add(resB);
            if (!resB.Passed) allPassed = false;
        }

        // Test C: Audio interruption during active recording
        if (filter == "ALL" || filter == "C")
        {
            Console.WriteLine("\n>>> Running Test C: Audio interruption during active recording...");
            var resC = await RunTestCAsync(cameraService, camera, mjpg1080, audioDevice);
            results.Add(resC);
            if (!resC.Passed) allPassed = false;
        }

        // Test D: Repeated Stop requests & concurrency idempotency
        if (filter == "ALL" || filter == "D")
        {
            Console.WriteLine("\n>>> Running Test D: Repeated Stop requests & shutdown idempotency...");
            var resD = await RunTestDAsync(cameraService, camera, mjpg1080, audioDevice);
            results.Add(resD);
            if (!resD.Passed) allPassed = false;
        }

        // Test E: Resource pressure and recovery
        if (filter == "ALL" || filter == "E")
        {
            Console.WriteLine("\n>>> Running Test E: Resource pressure (synthetic CPU load) & recovery recording...");
            var resE = await RunTestEAsync(cameraService, camera, mjpg1080, audioDevice);
            results.AddRange(resE);
            if (resE.Any(r => !r.Passed)) allPassed = false;
        }

        // Generate summary report
        WriteSummaryReport(results);

        Console.WriteLine("\n==================================================================");
        Console.WriteLine(allPassed ? "  ALL PHASE 40D INTERRUPTION TESTS PASSED!" : "  PHASE 40D COMPLETED WITH SOME WARNINGS / FAILURES");
        Console.WriteLine("==================================================================");

        return allPassed;
    }

    private static async Task<List<Phase40DTestScenarioResult>> RunTestAAsync(
        MediaFoundationCameraService cameraService,
        CameraDevice camera,
        CameraFormat mjpg1080,
        AudioDeviceInfo audioDevice)
    {
        var list = new List<Phase40DTestScenarioResult>();

        // Start camera stream once for both runs
        await cameraService.StartCaptureAsync(camera, mjpg1080);
        await Task.Delay(1500);

        // Run 1: 35s recording
        string mp4Path1 = Path.Combine(OutputDir, "Phase40D_TestA_Run1_35s.mp4");
        Console.WriteLine($"  [Test A1] Starting Recording Session 1 (35s)...");
        await cameraService.StartRecordingAsync(mp4Path1, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);
        await Task.Delay(35000);

        var sw1 = Stopwatch.StartNew();
        await cameraService.StopRecordingAsync();
        sw1.Stop();
        Console.WriteLine($"  [Test A1] Stopped Session 1 in {sw1.ElapsedMilliseconds} ms.");

        var res1 = AuditRecording("TEST_A1", "Test A Session 1 (35s)", mp4Path1, sw1.ElapsedMilliseconds, cameraService);
        list.Add(res1);

        // Run 2: Immediately start Recording Session 2 without stopping or restarting camera stream
        string mp4Path2 = Path.Combine(OutputDir, "Phase40D_TestA_Run2_35s.mp4");
        Console.WriteLine($"  [Test A2] Immediately Starting Recording Session 2 (35s)...");
        await cameraService.StartRecordingAsync(mp4Path2, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);
        await Task.Delay(35000);

        var sw2 = Stopwatch.StartNew();
        await cameraService.StopRecordingAsync();
        sw2.Stop();
        Console.WriteLine($"  [Test A2] Stopped Session 2 in {sw2.ElapsedMilliseconds} ms.");

        await cameraService.StopCaptureAsync();

        var res2 = AuditRecording("TEST_A2", "Test A Session 2 (35s Immediate Restart)", mp4Path2, sw2.ElapsedMilliseconds, cameraService);
        list.Add(res2);

        return list;
    }

    private static async Task<Phase40DTestScenarioResult> RunTestBAsync(
        MediaFoundationCameraService cameraService,
        CameraDevice camera,
        CameraFormat mjpg1080,
        AudioDeviceInfo audioDevice)
    {
        string mp4Path = Path.Combine(OutputDir, "Phase40D_TestB_CameraDisconnect.mp4");
        Console.WriteLine($"  [Test B] Starting camera capture and recording...");

        await cameraService.StartCaptureAsync(camera, mjpg1080);
        await Task.Delay(1500);

        await cameraService.StartRecordingAsync(mp4Path, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);
        Console.WriteLine($"  [Test B] Recording active. Capturing for 6.0 seconds...");
        await Task.Delay(6000);

        Console.WriteLine($"  [Test B] Severing camera capture stream mid-recording (simulating USB disconnect)...");
        await cameraService.StopCaptureAsync();

        Console.WriteLine($"  [Test B] Calling StopRecordingAsync() on severed capture session...");
        var sw = Stopwatch.StartNew();
        await cameraService.StopRecordingAsync();
        sw.Stop();
        Console.WriteLine($"  [Test B] Stop completed in {sw.ElapsedMilliseconds} ms. IsRecording={cameraService.IsRecording}.");

        // Verify camera can re-spinup cleanly
        Console.WriteLine($"  [Test B] Verifying camera re-spinup / recovery after disconnect...");
        bool recovered = false;
        try
        {
            await cameraService.StartCaptureAsync(camera, mjpg1080);
            await Task.Delay(2000);
            await cameraService.StopCaptureAsync();
            recovered = true;
            Console.WriteLine($"  [Test B] Camera recovery verified successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [Test B] Camera recovery failed: {ex.Message}");
        }

        var res = AuditRecording("TEST_B", "Test B Camera Stream Interruption", mp4Path, sw.ElapsedMilliseconds, cameraService);
        if (!recovered) res.Passed = false;
        res.Notes = recovered ? "Camera cleanly recovered after interruption" : "Camera failed to recover";
        return res;
    }

    private static async Task<Phase40DTestScenarioResult> RunTestCAsync(
        MediaFoundationCameraService cameraService,
        CameraDevice camera,
        CameraFormat mjpg1080,
        AudioDeviceInfo audioDevice)
    {
        string mp4Path = Path.Combine(OutputDir, "Phase40D_TestC_AudioInterrupt.mp4");
        Console.WriteLine($"  [Test C] Starting capture and recording with audio...");

        await cameraService.StartCaptureAsync(camera, mjpg1080);
        await Task.Delay(1500);

        await cameraService.StartRecordingAsync(mp4Path, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);
        Console.WriteLine($"  [Test C] Recording active for 6.0 seconds with normal audio...");
        await Task.Delay(6000);

        Console.WriteLine($"  [Test C] Interrupting audio capture service mid-recording (stopping WASAPI stream)...");
        cameraService.AudioCapture.Stop();

        Console.WriteLine($"  [Test C] Audio severed. Continuing video recording for 10.0 seconds to test non-blocking video...");
        await Task.Delay(10000);

        var sw = Stopwatch.StartNew();
        await cameraService.StopRecordingAsync();
        sw.Stop();
        Console.WriteLine($"  [Test C] Stop completed in {sw.ElapsedMilliseconds} ms.");

        await cameraService.StopCaptureAsync();

        var res = AuditRecording("TEST_C", "Test C Audio Interruption Resilience", mp4Path, sw.ElapsedMilliseconds, cameraService);
        return res;
    }

    private static async Task<Phase40DTestScenarioResult> RunTestDAsync(
        MediaFoundationCameraService cameraService,
        CameraDevice camera,
        CameraFormat mjpg1080,
        AudioDeviceInfo audioDevice)
    {
        string mp4Path = Path.Combine(OutputDir, "Phase40D_TestD_RepeatedStop.mp4");
        Console.WriteLine($"  [Test D] Starting capture and recording...");

        await cameraService.StartCaptureAsync(camera, mjpg1080);
        await Task.Delay(1500);

        await cameraService.StartRecordingAsync(mp4Path, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);
        Console.WriteLine($"  [Test D] Recording for 4.0 seconds...");
        await Task.Delay(4000);

        Console.WriteLine($"  [Test D] Dispatching 5 concurrent StopRecordingAsync() requests simultaneously...");
        var sw = Stopwatch.StartNew();
        var tasks = new Task[5];
        for (int i = 0; i < 5; i++)
        {
            tasks[i] = Task.Run(async () => await cameraService.StopRecordingAsync());
        }
        await Task.WhenAll(tasks);
        sw.Stop();
        Console.WriteLine($"  [Test D] All 5 concurrent stops completed cleanly in {sw.ElapsedMilliseconds} ms. No deadlock.");

        await cameraService.StopCaptureAsync();

        var res = AuditRecording("TEST_D", "Test D Repeated Stop & Concurrency Idempotency", mp4Path, sw.ElapsedMilliseconds, cameraService);
        return res;
    }

    private static async Task<List<Phase40DTestScenarioResult>> RunTestEAsync(
        MediaFoundationCameraService cameraService,
        CameraDevice camera,
        CameraFormat mjpg1080,
        AudioDeviceInfo audioDevice)
    {
        var list = new List<Phase40DTestScenarioResult>();

        // 1. Synthetic load recording (35s)
        using var loadCts = new CancellationTokenSource();
        StartSyntheticLoad(loadCts.Token);

        string mp4PathE1 = Path.Combine(OutputDir, "Phase40D_TestE_LoadRun.mp4");
        Console.WriteLine($"  [Test E1] Starting recording under active synthetic multi-core CPU load (35s)...");

        await cameraService.StartCaptureAsync(camera, mjpg1080);
        await Task.Delay(1500);

        await cameraService.StartRecordingAsync(mp4PathE1, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);
        await Task.Delay(35000);

        var swE1 = Stopwatch.StartNew();
        await cameraService.StopRecordingAsync();
        swE1.Stop();
        Console.WriteLine($"  [Test E1] Stopped in {swE1.ElapsedMilliseconds} ms under CPU load.");

        loadCts.Cancel();
        Console.WriteLine($"  [Test E1] Synthetic load cancelled.");

        var resE1 = AuditRecording("TEST_E1", "Test E1 Recording Under CPU Load (35s)", mp4PathE1, swE1.ElapsedMilliseconds, cameraService);
        list.Add(resE1);

        // 2. Immediate recovery run without load (15s)
        string mp4PathE2 = Path.Combine(OutputDir, "Phase40D_TestE_RecoveryRun.mp4");
        Console.WriteLine($"  [Test E2] Starting recovery recording without load (15s)...");

        await cameraService.StartRecordingAsync(mp4PathE2, RecordingMode.OriginalCamera, RecordingCadenceMode.Capture_Faithful_VFR, true, audioDevice);
        await Task.Delay(15000);

        var swE2 = Stopwatch.StartNew();
        await cameraService.StopRecordingAsync();
        swE2.Stop();
        Console.WriteLine($"  [Test E2] Stopped recovery run in {swE2.ElapsedMilliseconds} ms.");

        await cameraService.StopCaptureAsync();

        var resE2 = AuditRecording("TEST_E2", "Test E2 Immediate Recovery Recording (15s)", mp4PathE2, swE2.ElapsedMilliseconds, cameraService);
        list.Add(resE2);

        return list;
    }

    private static void StartSyntheticLoad(CancellationToken token)
    {
        for (int i = 0; i < 2; i++)
        {
            Task.Run(() =>
            {
                byte[] data = new byte[65536];
                Random.Shared.NextBytes(data);
                while (!token.IsCancellationRequested)
                {
                    _ = SHA256.HashData(data);
                    Thread.SpinWait(500);
                }
            }, token);
        }
    }

    private static Phase40DTestScenarioResult AuditRecording(
        string id,
        string desc,
        string mp4Path,
        long stopMs,
        MediaFoundationCameraService cameraService)
    {
        var res = new Phase40DTestScenarioResult
        {
            ScenarioId = id,
            Description = desc,
            Mp4Path = mp4Path,
            FinalizationElapsedMs = stopMs
        };

        if (!File.Exists(mp4Path))
        {
            res.Passed = false;
            res.Notes = "Output MP4 file does not exist";
            return res;
        }

        var fi = new FileInfo(mp4Path);
        res.FileSizeBytes = fi.Length;

        // Verify moov atom
        res.HasMoovAtom = CheckMoovAtom(mp4Path);

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

        // Evaluation criteria:
        // 1. File exists and size > 50 KB
        // 2. Has valid moov atom
        // 3. Clean video decode
        // 4. Clean audio decode (or if audio was interrupted, clean decode of what was recorded)
        // 5. Stop elapsed < 5000 ms
        res.Passed = res.FileSizeBytes > 50_000 &&
                     res.HasMoovAtom &&
                     res.CleanVideoDecode &&
                     res.CleanAudioDecode &&
                     res.FinalizationElapsedMs < 5000;

        Console.WriteLine($"    [Audit {id}] Size: {res.FileSizeBytes:N0} bytes | Moov: {res.HasMoovAtom} | Video: {res.VideoDurationSeconds:F2}s ({res.FramesCaptured} frames) | Audio: {res.AudioDurationSeconds:F2}s | Delta: {res.TrackDeltaMs:F2} ms | Stop: {res.FinalizationElapsedMs} ms | Result: {(res.Passed ? "PASS" : "FAIL")}");

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

    private static void ProbeMediaFile(string mp4Path, Phase40DTestScenarioResult res)
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
            if (p != null)
            {
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 && string.IsNullOrWhiteSpace(err);
            }
        }
        catch { }
        return false;
    }

    private static void WriteSummaryReport(List<Phase40DTestScenarioResult> results)
    {
        string reportPath = Path.Combine(OutputDir, "Phase40D_Summary_Report.txt");
        var sb = new StringBuilder();
        sb.AppendLine("==================================================================");
        sb.AppendLine("  WebcamLab Phase 40D Interruption & Edge-Case Reliability Summary");
        sb.AppendLine($"  Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("==================================================================");
        sb.AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine($"Scenario: {r.ScenarioId} — {r.Description}");
            sb.AppendLine($"  Result:        {(r.Passed ? "PASS" : "FAIL")}");
            sb.AppendLine($"  File:          {r.Mp4Path}");
            sb.AppendLine($"  Size:          {r.FileSizeBytes:N0} bytes");
            sb.AppendLine($"  Moov Atom:     {r.HasMoovAtom}");
            sb.AppendLine($"  Video Dur:     {r.VideoDurationSeconds:F3} s ({r.FramesCaptured} captured, {r.FramesDropped} dropped)");
            sb.AppendLine($"  Audio Dur:     {r.AudioDurationSeconds:F3} s");
            sb.AppendLine($"  Track Delta:   {r.TrackDeltaMs:F2} ms");
            sb.AppendLine($"  Decode Clean:  Video={r.CleanVideoDecode}, Audio={r.CleanAudioDecode}");
            sb.AppendLine($"  Stop Latency:  {r.FinalizationElapsedMs} ms");
            if (!string.IsNullOrEmpty(r.Notes)) sb.AppendLine($"  Notes:         {r.Notes}");
            sb.AppendLine();
        }
        File.WriteAllText(reportPath, sb.ToString());
        Console.WriteLine($"\nSummary report saved to: {reportPath}");
    }
}
