using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebcamLab.Capture;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Models;
using WebcamLab.Core.Session;
using WebcamLab.Core.Validation;

namespace WebcamLab.Core.ReverseEngineering;

public class ForensicReport
{
    public string Title { get; set; } = "";
    public List<string> Findings { get; set; } = [];
}

public static class Phase18ForensicRunner
{
    private static readonly StringBuilder _sb = new();

    private static void Log(string msg)
    {
        Console.WriteLine(msg);
        _sb.AppendLine(msg);
    }

    public static async Task<bool> RunAllAsync()
    {
        _sb.Clear();
        Log("");
        Log("==================================================================");
        Log("  WebcamLab Phase 18.1: Final A/V Timing Closure & Session Audit");
        Log("  Target Device: Dell UZ2315H (Realtek RTS5875 + Integrated Audio)");
        Log("==================================================================");

        string testEPath = @"E:\WebcamLab_Phase17\Phase17_TEST_E_MJPG_CFR_Compatibility_20261025_235025.mp4";
        string testERemediatedPath = @"E:\WebcamLab_Phase17\Phase17_TEST_E_MJPG_CFR_Compatibility_20261027_133323.mp4";
        string testBPath = @"E:\WebcamLab_Phase17\Phase17_TEST_B_MJPG_Capture_Faithful_VFR_20261026_011840.mp4";
        string testCPath = @"E:\WebcamLab_Phase17\Phase17_TEST_C_YUY2_Capture_Faithful_VFR_20261026_010057.mp4";
        string testAPath = @"E:\WebcamLab_Phase17\Phase17_TEST_A_MJPG_Capture_Faithful_VFR_20261026_004322.mp4";

        var ffmpeg = VideoRecorder.FindFfmpegPath() ?? "ffmpeg.exe";
        string outDir = @"E:\WebcamLab_Phase17";
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

        // Task 1: Audit Test E (CFR 21.17s Desynchronization Baseline vs Remediated)
        Log("\n>>> TASK 1: Forensically Auditing Historical Test E Recordings...");
        Log("  [1a] Auditing Phase 17 Unremediated Baseline:");
        AuditTestE(testEPath, ffmpeg);
        if (File.Exists(testERemediatedPath))
        {
            Log("\n  [1b] Auditing Phase 18 Remediated Hardware Verification (Pre-Closure):");
            AuditTestE(testERemediatedPath, ffmpeg);
        }

        // Task 2: Reconcile Drift Measurements on Test C (YUY2 15m) & Test A (MJPEG 15m)
        Log("\n>>> TASK 2: Reconciling Drift & Clapper Math on Test C & Test A...");
        AuditDrift(testCPath, testAPath, ffmpeg);

        // Task 3: Audit Test B (30-Minute MJPEG Frame Count Anomaly)
        Log("\n>>> TASK 3: Auditing Test B 30-Minute Frame Count & Session Lock...");
        AuditTestB(testBPath, ffmpeg);

        // Task 4: Run Live Phase 18.1 CFR Hardware Verification
        Log("\n>>> TASK 4: Running Live Phase 18.1 Hardware Tests on Dell UZ2315H...");
        await RunLivePhase18_1HardwareTestsAsync(outDir, ffmpeg);

        try
        {
            string outPath = Path.Combine(AppContext.BaseDirectory, "Phase18_1_Audit.txt");
            File.WriteAllText(outPath, _sb.ToString());
            if (Directory.Exists(outDir))
            {
                File.WriteAllText(Path.Combine(outDir, "Phase18_1_Audit.txt"), _sb.ToString());
            }
        }
        catch { }

        return true;
    }

    private static async Task RunLivePhase18_1HardwareTestsAsync(string outDir, string ffmpeg)
    {
        var camService = new MediaFoundationCameraService();
        var cameras = await camService.EnumerateCamerasAsync();
        var dellCam = cameras.FirstOrDefault(c => c.IsDellUZ2315H) ?? cameras.FirstOrDefault();
        if (dellCam == null)
        {
            Log("  [SKIP] Dell UZ2315H camera not detected for live hardware tests.");
            return;
        }

        var audioDevices = await AudioDeviceService.EnumerateCaptureDevicesAsync();
        var dellAudio = audioDevices.FirstOrDefault(d => d.IsDellUZ2315H) ?? audioDevices.FirstOrDefault(d => d.IsDefault);
        if (dellAudio == null)
        {
            Log("  [SKIP] Audio capture device not detected for live hardware tests.");
            return;
        }

        var formats = await camService.GetSupportedFormatsAsync(dellCam);
        var mjpg1080 = formats.FirstOrDefault(f => f.Width == 1920 && f.Height == 1080 && f.FourCC == "MJPG");
        if (mjpg1080 == null)
        {
            Log("  [SKIP] 1080p MJPEG format not found.");
            return;
        }

        // Test 1: Short CFR Partial Slot Test (5.15s @ 30 FPS target)
        Log("\n  [Test 4a: Live Short CFR Partial-Slot Recording (5.15s Target)]");
        string shortMp4 = Path.Combine(outDir, $"Phase18_1_TEST_SHORT_CFR_MJPG_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        await camService.StartCaptureAsync(dellCam, mjpg1080);
        await Task.Delay(1500); // Sensor auto-exposure warmup

        var swShort = Stopwatch.StartNew();
        await camService.StartRecordingAsync(shortMp4, RecordingMode.OriginalCamera, RecordingCadenceMode.CFR_Compatibility, enableAudio: true, audioDevice: dellAudio);
        while (swShort.Elapsed.TotalSeconds < 5.15)
        {
            await Task.Delay(50);
        }
        await camService.StopRecordingAsync();
        await camService.StopCaptureAsync();
        swShort.Stop();

        Log($"    Target Elapsed: 5.15s, Achieved Elapsed: {swShort.Elapsed.TotalSeconds:F3}s");
        AuditTestE(shortMp4, ffmpeg);

        // Test 2: Two-Minute CFR Recording with 5 Scheduled Clappers (3s, 30s, 60s, 90s, 115s)
        Log("\n  [Test 4b: Live 2-Minute CFR Recording with Multi-Point Clapper]");
        string twoMinMp4 = Path.Combine(outDir, $"Phase18_1_TEST_E_CFR_MJPG_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        await camService.StartCaptureAsync(dellCam, mjpg1080);
        await Task.Delay(1500); // Sensor warmup

        var clapperSchedule = new List<double> { 3.0, 30.0, 60.0, 90.0, 115.0 };
        var scheduledEvents = new List<ClapperEventRecord>();
        int nextClapper = 0;

        var swTwoMin = Stopwatch.StartNew();
        await camService.StartRecordingAsync(twoMinMp4, RecordingMode.OriginalCamera, RecordingCadenceMode.CFR_Compatibility, enableAudio: true, audioDevice: dellAudio);
        Console.Write("    Recording 2-min CFR... Progress: ");

        while (swTwoMin.Elapsed.TotalSeconds < 120.0)
        {
            await Task.Delay(500);
            double el = swTwoMin.Elapsed.TotalSeconds;

            if (nextClapper < clapperSchedule.Count && el >= clapperSchedule[nextClapper])
            {
                var evt = PhysicalClapperHarness.TriggerClapper(nextClapper + 1, el);
                scheduledEvents.Add(evt);
                nextClapper++;
                Console.Write($"[CLAP #{nextClapper} @ {el:F1}s] ");
            }

            if ((int)el % 30 == 0 && (int)el > 0 && Math.Abs(el - Math.Round(el)) < 0.5)
            {
                Console.Write($"{(int)el}s.. ");
            }
        }
        Console.WriteLine("Done. Stopping...");

        await camService.StopRecordingAsync();
        await camService.StopCaptureAsync();
        swTwoMin.Stop();

        Log($"    Target: 120.0s, Achieved Wall Elapsed: {swTwoMin.Elapsed.TotalSeconds:F3}s");
        AuditTestE(twoMinMp4, ffmpeg, scheduledEvents);
    }

    private static void AuditTestE(string mp4Path, string ffmpeg, List<ClapperEventRecord>? clapperSchedule = null)
    {
        if (!File.Exists(mp4Path))
        {
            Log($"[FAIL] MP4 not found: {mp4Path}");
            return;
        }

        var fi = new FileInfo(mp4Path);
        Log($"  MP4: {mp4Path} ({fi.Length:N0} bytes)");

        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        double vidDur = 0.0, audDur = 0.0;
        double vidStart = 0.0, audStart = 0.0;
        int vidFrames = 0, audPackets = 0;

        if (File.Exists(ffprobe))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobe,
                    Arguments = $"-v error -show_entries stream=index,codec_type,start_time,duration,nb_frames -of json \"{mp4Path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                string json = proc?.StandardOutput.ReadToEnd() ?? "";
                proc?.WaitForExit(10000);
                using var doc = JsonDocument.Parse(json);
                foreach (var st in doc.RootElement.GetProperty("streams").EnumerateArray())
                {
                    string type = st.GetProperty("codec_type").GetString() ?? "";
                    string dur = st.TryGetProperty("duration", out var d) ? d.GetString() ?? "" : "";
                    string start = st.TryGetProperty("start_time", out var s) ? s.GetString() ?? "" : "";
                    string nb = st.TryGetProperty("nb_frames", out var n) ? n.GetString() ?? "" : "";
                    Log($"    Stream [{type}]: Start={start}s, Duration={dur}s, Frames={nb}");

                    if (type == "video")
                    {
                        double.TryParse(dur, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out vidDur);
                        double.TryParse(start, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out vidStart);
                        int.TryParse(nb, out vidFrames);
                    }
                    else if (type == "audio")
                    {
                        double.TryParse(dur, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out audDur);
                        double.TryParse(start, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out audStart);
                        int.TryParse(nb, out audPackets);
                    }
                }

                double durDeltaMs = Math.Abs(vidDur - audDur) * 1000.0;
                double startDeltaMs = Math.Abs(vidStart - audStart) * 1000.0;
                Log($"    [Stream Alignment] StartDelta={startDeltaMs:F2}ms, DurationDelta={durDeltaMs:F2}ms (Vid={vidDur:F3}s, Aud={audDur:F3}s)");
            }
            catch { }
        }

        // Clapper Events
        var eventsToAnalyze = clapperSchedule ?? new List<ClapperEventRecord>
        {
            new() { EventIndex = 1, ScheduledTimeSec = 3.0 },
            new() { EventIndex = 2, ScheduledTimeSec = 60.0 },
            new() { EventIndex = 3, ScheduledTimeSec = 115.0 }
        };

        var clapperReport = PhysicalClapperHarness.AnalyzeMp4ClapperEvents(mp4Path, eventsToAnalyze, ffmpeg);

        Log("  [Clapper Analysis]");
        foreach (var evt in clapperReport.Events)
        {
            string audioStr = evt.BeepDetected ? $"{evt.DetectedAudioTimeSec:F3}s" : "NotDetected";
            string offsetStr = evt.BeepDetected ? $"{evt.PhysicalOffsetMs:F2}ms" : "N/A";
            Log($"    Event #{evt.EventIndex} (Scheduled: {evt.ScheduledTimeSec:F1}s): AudioOnset={audioStr}, VideoPTS={evt.DetectedVideoTimeSec:F3}s, Offset={offsetStr}");
        }
    }

    private static void AuditDrift(string testCPath, string testAPath, string ffmpeg)
    {
        if (File.Exists(testCPath))
        {
            Log("\n  [Test C: 15-Minute 1080p YUY2 Analysis]");
            var clapperEventsC = new List<ClapperEventRecord>
            {
                new() { EventIndex = 1, ScheduledTimeSec = 3.0 },
                new() { EventIndex = 2, ScheduledTimeSec = 450.0 },
                new() { EventIndex = 3, ScheduledTimeSec = 895.0 }
            };
            var repC = PhysicalClapperHarness.AnalyzeMp4ClapperEvents(testCPath, clapperEventsC, ffmpeg);
            foreach (var evt in repC.Events)
            {
                string aStr = evt.BeepDetected ? $"{evt.DetectedAudioTimeSec:F3}s" : "NotDetected";
                string oStr = evt.BeepDetected ? $"{evt.PhysicalOffsetMs:F2}ms" : "N/A";
                Log($"    Event #{evt.EventIndex} (Sched: {evt.ScheduledTimeSec:F1}s): Audio={aStr}, Video={evt.DetectedVideoTimeSec:F3}s, Offset={oStr}");
            }
            string cDriftStr = double.IsNaN(repC.PhysicalDriftMs) ? "N/A" : $"{repC.PhysicalDriftMs:F2}ms";
            string cRateStr = double.IsNaN(repC.PhysicalDriftRateMsPerMin) ? "N/A" : $"{repC.PhysicalDriftRateMsPerMin:F2}ms/min ({repC.PhysicalClockPpm:F1} ppm)";
            Log($"    Physical Drift: Initial={repC.InitialPhysicalOffsetMs:F2}ms, Final={repC.FinalPhysicalOffsetMs:F2}ms, NetDrift={cDriftStr}, Rate={cRateStr}");
        }

        if (File.Exists(testAPath))
        {
            Log("\n  [Test A: 15-Minute 1080p MJPEG Analysis]");
            var clapperEventsA = new List<ClapperEventRecord>
            {
                new() { EventIndex = 1, ScheduledTimeSec = 3.0 },
                new() { EventIndex = 2, ScheduledTimeSec = 450.0 },
                new() { EventIndex = 3, ScheduledTimeSec = 895.0 }
            };
            var repA = PhysicalClapperHarness.AnalyzeMp4ClapperEvents(testAPath, clapperEventsA, ffmpeg);
            foreach (var evt in repA.Events)
            {
                string aStr = evt.BeepDetected ? $"{evt.DetectedAudioTimeSec:F3}s" : "NotDetected";
                string oStr = evt.BeepDetected ? $"{evt.PhysicalOffsetMs:F2}ms" : "N/A";
                Log($"    Event #{evt.EventIndex} (Sched: {evt.ScheduledTimeSec:F1}s): Audio={aStr}, Video={evt.DetectedVideoTimeSec:F3}s, Offset={oStr}");
            }
            string aDriftStr = double.IsNaN(repA.PhysicalDriftMs) ? "N/A" : $"{repA.PhysicalDriftMs:F2}ms";
            string aRateStr = double.IsNaN(repA.PhysicalDriftRateMsPerMin) ? "N/A" : $"{repA.PhysicalDriftRateMsPerMin:F2}ms/min ({repA.PhysicalClockPpm:F1} ppm)";
            Log($"    Physical Drift: Initial={repA.InitialPhysicalOffsetMs:F2}ms, Final={repA.FinalPhysicalOffsetMs:F2}ms, NetDrift={aDriftStr}, Rate={aRateStr}");
        }
    }

    private static void AuditTestB(string mp4Path, string ffmpeg)
    {
        if (!File.Exists(mp4Path))
        {
            Log($"[FAIL] Test B MP4 not found: {mp4Path}");
            return;
        }

        var fi = new FileInfo(mp4Path);
        Log($"  MP4: {mp4Path} ({fi.Length:N0} bytes)");

        var clapperEventsB = new List<ClapperEventRecord>
        {
            new() { EventIndex = 1, ScheduledTimeSec = 3.0 },
            new() { EventIndex = 2, ScheduledTimeSec = 900.0 },
            new() { EventIndex = 3, ScheduledTimeSec = 1795.0 }
        };
        var repB = PhysicalClapperHarness.AnalyzeMp4ClapperEvents(mp4Path, clapperEventsB, ffmpeg);
        foreach (var evt in repB.Events)
        {
            string aStr = evt.BeepDetected ? $"{evt.DetectedAudioTimeSec:F3}s" : "NotDetected";
            string oStr = evt.BeepDetected ? $"{evt.PhysicalOffsetMs:F2}ms" : "N/A";
            Log($"    Event #{evt.EventIndex} (Sched: {evt.ScheduledTimeSec:F1}s): Audio={aStr}, Video={evt.DetectedVideoTimeSec:F3}s, Offset={oStr}");
        }
    }
}
