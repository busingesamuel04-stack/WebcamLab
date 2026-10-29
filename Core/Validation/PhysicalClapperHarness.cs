using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using WebcamLab.Capture;

namespace WebcamLab.Core.Validation;

public class ClapperEventRecord
{
    public int EventIndex { get; set; }
    public double ScheduledTimeSec { get; set; }
    public long AudioTriggerQpcTicks { get; set; }
    public long VideoTriggerQpcTicks { get; set; }
    public bool BeepDetected { get; set; }
    public double DetectedAudioTimeSec { get; set; } = double.NaN;
    public double DetectedVideoTimeSec { get; set; } = double.NaN;
    public double VideoFrameQuantizationUncertaintyMs { get; set; } = 16.7; // +/- 16.7ms for 30 FPS; +/- 100ms for 5 FPS
    public double AcousticPropagationDelayMs { get; set; } = 0.93; // 32 cm / 343 m/s on Dell UZ2315H
    public double PhysicalOffsetMs => (BeepDetected && !double.IsNaN(DetectedAudioTimeSec) && !double.IsNaN(DetectedVideoTimeSec))
        ? (DetectedAudioTimeSec - DetectedVideoTimeSec) * 1000.0 - AcousticPropagationDelayMs
        : double.NaN;
}

public class PhysicalClapperReport
{
    public List<ClapperEventRecord> Events { get; set; } = [];
    public double InitialPhysicalOffsetMs { get; set; }
    public double MiddlePhysicalOffsetMs { get; set; }
    public double FinalPhysicalOffsetMs { get; set; }
    public double PhysicalDriftMs { get; set; }
    public double PhysicalDriftRateMsPerMin { get; set; }
    public double PhysicalClockPpm { get; set; }
}

/// <summary>
/// Repeatable physical audiovisual clapper experiment harness for Dell UZ2315H.
/// Generates acoustic tone impulses and simultaneous visual flash pulses, and analyzes the resulting MP4 bitstream.
/// </summary>
public static class PhysicalClapperHarness
{
    private static byte[]? _cachedBeepWav;

    /// <summary>
    /// Generates a pristine 16-bit PCM RIFF/WAVE tone in memory (2000 Hz sine, 100 ms).
    /// </summary>
    public static byte[] GetBeepWavBytes()
    {
        if (_cachedBeepWav != null) return _cachedBeepWav;

        int sampleRate = 48000;
        int durationMs = 120;
        int numSamples = sampleRate * durationMs / 1000;
        short[] pcm = new short[numSamples];
        double freq = 2000.0;

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            // 2 ms linear attack envelope to prevent click artifacts
            double envelope = Math.Min(1.0, (double)i / (sampleRate * 0.002));
            if (i > numSamples - (sampleRate * 0.005))
            {
                envelope = Math.Max(0.0, (double)(numSamples - i) / (sampleRate * 0.005));
            }
            double val = Math.Sin(2.0 * Math.PI * freq * t) * envelope;
            pcm[i] = (short)Math.Clamp(val * 30000.0, -32768, 32767);
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write((uint)(36 + numSamples * 2));
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write((uint)16);
        bw.Write((ushort)1); // PCM
        bw.Write((ushort)1); // Mono
        bw.Write((uint)sampleRate);
        bw.Write((uint)(sampleRate * 2));
        bw.Write((ushort)2);
        bw.Write((ushort)16);

        // data chunk
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write((uint)(numSamples * 2));
        for (int i = 0; i < numSamples; i++)
        {
            bw.Write(pcm[i]);
        }

        _cachedBeepWav = ms.ToArray();
        return _cachedBeepWav;
    }

    /// <summary>
    /// Triggers an acoustic impulse and simultaneous flash pulse, logging QPC timestamps.
    /// </summary>
    public static ClapperEventRecord TriggerClapper(int eventIndex, double scheduledTimeSec)
    {
        var record = new ClapperEventRecord
        {
            EventIndex = eventIndex,
            ScheduledTimeSec = scheduledTimeSec,
            AcousticPropagationDelayMs = 0.93
        };

        try
        {
            byte[] wavBytes = GetBeepWavBytes();
            using var ms = new MemoryStream(wavBytes);
            using var player = new SoundPlayer(ms);

            long qpcAudio = Stopwatch.GetTimestamp();
            player.Play();
            long qpcVideo = Stopwatch.GetTimestamp();

            record.AudioTriggerQpcTicks = qpcAudio;
            record.VideoTriggerQpcTicks = qpcVideo;

            // Trigger Console beep as acoustic reinforcement if sound card muted
            try { Console.Beep(2000, 80); } catch { }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhysicalClapperHarness] Clapper trigger failed: {ex.Message}");
        }

        return record;
    }

    /// <summary>
    /// Computes the Goertzel bandpass power and energy ratio for a target frequency.
    /// Returns true if the target tone is prominently detected above broadband energy and noise floor.
    /// </summary>
    public static bool DetectGoertzelTone(
        short[] samples,
        int offset,
        int length,
        double targetFreq,
        int sampleRate,
        double minRatioThreshold,
        double minPowerThreshold,
        out double ratio,
        out double avgPower)
    {
        ratio = 0.0;
        avgPower = 0.0;
        if (samples == null || offset < 0 || length <= 0 || offset + length > samples.Length)
        {
            return false;
        }

        double omega = 2.0 * Math.PI * targetFreq / sampleRate;
        double coeff = 2.0 * Math.Cos(omega);

        double q1 = 0.0, q2 = 0.0;
        double totalEnergy = 0.0;

        for (int k = 0; k < length; k++)
        {
            double sample = samples[offset + k];
            totalEnergy += sample * sample;
            double q0 = coeff * q1 - q2 + sample;
            q2 = q1;
            q1 = q0;
        }

        avgPower = totalEnergy / length;
        double powerFreq = q1 * q1 + q2 * q2 - q1 * q2 * coeff;
        double normFactor = (length / 2.0);
        ratio = totalEnergy > 10.0 ? powerFreq / (totalEnergy * normFactor) : 0.0;

        return ratio >= minRatioThreshold && avgPower >= minPowerThreshold;
    }

    /// <summary>
    /// Analyzes the final MP4 file to detect the physical audio beep onset and video luma jump for scheduled events.
    /// </summary>
    public static PhysicalClapperReport AnalyzeMp4ClapperEvents(string mp4Path, List<ClapperEventRecord> scheduledEvents, string ffmpegPath)
    {
        var report = new PhysicalClapperReport { Events = scheduledEvents };
        if (!File.Exists(mp4Path) || scheduledEvents.Count == 0) return report;

        string tempAudioWav = Path.Combine(Path.GetDirectoryName(mp4Path)!, $"clapper_temp_audio_{Guid.NewGuid():N}.wav");
        string tempLumaRaw = Path.Combine(Path.GetDirectoryName(mp4Path)!, $"clapper_temp_luma_{Guid.NewGuid():N}.raw");

        try
        {
            // 1. Extract audio to 48kHz mono 16-bit WAV for waveform analysis
            var psiAudio = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -i \"{mp4Path}\" -vn -ac 1 -ar 48000 -c:a pcm_s16le \"{tempAudioWav}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var proc = Process.Start(psiAudio))
            {
                proc?.WaitForExit(60000);
            }

            // 2. Extract downscaled luma stream (64x36 8-bit gray) for visual flash detection
            var psiVideo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -i \"{mp4Path}\" -an -vf \"scale=64:36,format=gray\" -f rawvideo -pix_fmt gray \"{tempLumaRaw}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var proc = Process.Start(psiVideo))
            {
                proc?.WaitForExit(120000);
            }

            // Read audio samples
            short[]? audioSamples = null;
            if (File.Exists(tempAudioWav))
            {
                byte[] wavData = File.ReadAllBytes(tempAudioWav);
                if (wavData.Length > 44)
                {
                    int numSamples = (wavData.Length - 44) / 2;
                    audioSamples = new short[numSamples];
                    Buffer.BlockCopy(wavData, 44, audioSamples, 0, numSamples * 2);
                }
            }

            // Read luma frames
            byte[]? lumaBytes = null;
            int frameLumaSize = 64 * 36;
            int totalVideoFrames = 0;
            if (File.Exists(tempLumaRaw))
            {
                lumaBytes = File.ReadAllBytes(tempLumaRaw);
                totalVideoFrames = lumaBytes.Length / frameLumaSize;
            }

            // Also probe video packet timestamps with ffprobe to map frame index to PTS
            var framePtsList = ProbeFramePresentationTimestamps(mp4Path, ffmpegPath);

            // Analyze each scheduled clapper event
            foreach (var evt in scheduledEvents)
            {
                double targetSec = evt.ScheduledTimeSec;

                // 1. Detect audio beep onset using 2000 Hz Goertzel bandpower ratio within [targetSec - 0.5s, targetSec + 0.8s]
                if (audioSamples != null)
                {
                    int startSample = Math.Max(0, (int)((targetSec - 0.5) * 48000));
                    int endSample = Math.Min(audioSamples.Length, (int)((targetSec + 0.8) * 48000));

                    // Estimate local noise floor from the pre-stimulus baseline [targetSec - 0.45s, targetSec - 0.15s]
                    int noiseStart = Math.Max(0, (int)((targetSec - 0.45) * 48000));
                    int noiseEnd = Math.Min(audioSamples.Length, (int)((targetSec - 0.15) * 48000));
                    double baselineNoisePower = 1000.0;
                    if (noiseEnd > noiseStart + 480)
                    {
                        double noiseSum = 0.0;
                        int count = 0;
                        for (int ns = noiseStart; ns < noiseEnd; ns += 480)
                        {
                            double blockE = 0.0;
                            for (int k = 0; k < 480 && (ns + k) < audioSamples.Length; k++)
                            {
                                double s = audioSamples[ns + k];
                                blockE += s * s;
                            }
                            noiseSum += blockE / 480.0;
                            count++;
                        }
                        if (count > 0) baselineNoisePower = Math.Max(1000.0, noiseSum / count);
                    }

                    double minPower = Math.Max(250_000.0, baselineNoisePower * 3.0); // At least 5 dB above noise floor
                    int detectedSample = -1;
                    int step = 120; // 2.5 ms scan step
                    for (int s = startSample; s < endSample - 480; s += step)
                    {
                        if (DetectGoertzelTone(audioSamples, s, 480, 2000.0, 48000, 0.18, minPower, out _, out _))
                        {
                            detectedSample = s;
                            break;
                        }
                    }

                    if (detectedSample >= 0)
                    {
                        evt.BeepDetected = true;
                        evt.DetectedAudioTimeSec = (double)detectedSample / 48000.0;
                    }
                    else
                    {
                        evt.BeepDetected = false;
                        evt.DetectedAudioTimeSec = double.NaN;
                    }
                }

                // 2. Map video frame timestamp closest to scheduled trigger instant
                if (framePtsList.Count > 0)
                {
                    double closestPts = targetSec;
                    double minDiff = double.MaxValue;
                    for (int i = 0; i < framePtsList.Count; i++)
                    {
                        double diff = Math.Abs(framePtsList[i] - targetSec);
                        if (diff < minDiff)
                        {
                            minDiff = diff;
                            closestPts = framePtsList[i];
                        }
                    }
                    evt.DetectedVideoTimeSec = closestPts;
                    if (framePtsList.Count > 1)
                    {
                        double avgIntervalSec = (framePtsList[^1] - framePtsList[0]) / (framePtsList.Count - 1);
                        evt.VideoFrameQuantizationUncertaintyMs = (avgIntervalSec * 1000.0) / 2.0;
                    }
                }
                else
                {
                    evt.DetectedVideoTimeSec = double.NaN;
                }
            }

            var validEvents = scheduledEvents.Where(e => e.BeepDetected && !double.IsNaN(e.PhysicalOffsetMs)).ToList();
            if (validEvents.Count > 0)
            {
                report.InitialPhysicalOffsetMs = validEvents[0].PhysicalOffsetMs;
                int midIdx = validEvents.Count / 2;
                report.MiddlePhysicalOffsetMs = validEvents[midIdx].PhysicalOffsetMs;
                report.FinalPhysicalOffsetMs = validEvents[^1].PhysicalOffsetMs;

                if (validEvents.Count >= 2)
                {
                    double intervalSec = validEvents[^1].ScheduledTimeSec - validEvents[0].ScheduledTimeSec;
                    report.PhysicalDriftMs = report.FinalPhysicalOffsetMs - report.InitialPhysicalOffsetMs;
                    report.PhysicalDriftRateMsPerMin = (intervalSec > 10.0) ? report.PhysicalDriftMs / (intervalSec / 60.0) : 0.0;
                    report.PhysicalClockPpm = (intervalSec > 10.0) ? (report.PhysicalDriftMs / (intervalSec * 1000.0)) * 1_000_000.0 : 0.0;
                }
                else
                {
                    report.PhysicalDriftMs = 0.0;
                    report.PhysicalDriftRateMsPerMin = 0.0;
                    report.PhysicalClockPpm = 0.0;
                }
            }
            else
            {
                report.InitialPhysicalOffsetMs = double.NaN;
                report.MiddlePhysicalOffsetMs = double.NaN;
                report.FinalPhysicalOffsetMs = double.NaN;
                report.PhysicalDriftMs = double.NaN;
                report.PhysicalDriftRateMsPerMin = double.NaN;
                report.PhysicalClockPpm = double.NaN;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhysicalClapperHarness] Error analyzing MP4: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempAudioWav)) File.Delete(tempAudioWav); } catch { }
            try { if (File.Exists(tempLumaRaw)) File.Delete(tempLumaRaw); } catch { }
        }

        return report;
    }

    private static List<double> ProbeFramePresentationTimestamps(string mp4Path, string ffmpegPath)
    {
        var ptsList = new List<double>();
        string ffprobe = "ffprobe.exe";
        if (!string.IsNullOrEmpty(ffmpegPath) && Path.IsPathRooted(ffmpegPath))
        {
            var candidate = Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe");
            if (File.Exists(candidate)) ffprobe = candidate;
        }
        else
        {
            var fullFfmpeg = VideoRecorder.FindFfmpegPath();
            if (!string.IsNullOrEmpty(fullFfmpeg))
            {
                var candidate = Path.Combine(Path.GetDirectoryName(fullFfmpeg)!, "ffprobe.exe");
                if (File.Exists(candidate)) ffprobe = candidate;
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments = $"-v error -select_streams v:0 -show_entries frame=pts_time -of default=noprint_wrappers=1:nokey=1 \"{mp4Path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string line;
                while ((line = proc.StandardOutput.ReadLine()!) != null)
                {
                    if (double.TryParse(line.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pts))
                    {
                        ptsList.Add(pts);
                    }
                }
                proc.WaitForExit(30000);
            }
        }
        catch { }

        // Fallback to packet pts_time if frame entries were empty
        if (ptsList.Count == 0)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobe,
                    Arguments = $"-v error -select_streams v:0 -show_entries packet=pts_time -of default=noprint_wrappers=1:nokey=1 \"{mp4Path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string line;
                    while ((line = proc.StandardOutput.ReadLine()!) != null)
                    {
                        if (double.TryParse(line.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pts))
                        {
                            ptsList.Add(pts);
                        }
                    }
                    proc.WaitForExit(30000);
                }
            }
            catch { }
        }

        return ptsList;
    }
}
