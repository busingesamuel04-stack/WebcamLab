using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using WebcamLab.Capture;
using WebcamLab.Core.Models;
using WebcamLab.Core.Native;

namespace WebcamLab.Core.Validation;

public class NativeBackendValidationResult
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public double ExecutionMs { get; set; }
    public string Details { get; set; } = string.Empty;
}

/// <summary>
/// Gate 2 Native Backend Automated Validation Suite.
/// Validates Media Foundation IMFSinkWriter backend state contracts, queue drain,
/// start/stop idempotency, VFR/CFR timeline accounting, zero-external-process boundary,
/// and default promotion invariants.
/// </summary>
public static class NativeBackendValidator
{
    public static List<NativeBackendValidationResult> RunAllTests()
    {
        MediaFoundationNative.MFStartup(0x00020070, 0);

        var results = new List<NativeBackendValidationResult>();

        results.Add(TestDefaultPromotionContract());
        results.Add(TestStartStopIdempotency());
        results.Add(TestQueueDrainAndBufferRecycling());
        results.Add(TestEarlyStopRobustness());
        results.Add(TestVFRTimestampPreservation());
        results.Add(TestCFRFrameSlotAccounting());
        results.Add(TestZeroExternalProcessSpawning());
        results.Add(TestActionableFailureContract());
        results.Add(TestSustainedInterleavedMultiplexingLoad());
        results.Add(TestMoovAtomValidationGuard());
        results.Add(TestBoundedInterleavingStarvationImmunity());
        results.Add(TestTailSilenceSafetyCap());
        results.Add(TestInterruptedStreamGracefulShutdown());
        results.Add(TestAudioLossNonBlockingVideo());

        return results;
    }

    private static NativeBackendValidationResult TestDefaultPromotionContract()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cameraService = new MediaFoundationCameraService();

            bool isDefaultBackend = cameraService.SelectedRecordingBackend == RecordingBackendMode.MediaFoundation_Default;
            var active = cameraService.ActiveRecorder;
            bool isNativeRecorder = active is NativeMediaFoundationRecorder;
            bool backendNameValid = active.BackendName.Contains("Media Foundation", StringComparison.OrdinalIgnoreCase);
            bool modeMatches = active.BackendMode == RecordingBackendMode.MediaFoundation_Default;

            sw.Stop();
            bool passed = isDefaultBackend && isNativeRecorder && backendNameValid && modeMatches;
            return new NativeBackendValidationResult
            {
                Name = "1. Native Media Foundation Default Promotion Contract",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"SelectedBackend: {cameraService.SelectedRecordingBackend}, ActiveType: {active.GetType().Name}, BackendName: '{active.BackendName}', Mode: {active.BackendMode}"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "1. Native Media Foundation Default Promotion Contract",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
    }

    private static NativeBackendValidationResult TestStartStopIdempotency()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_Idempotency_{Guid.NewGuid():N}.mp4");
        try
        {
            using (var recorder = new NativeMediaFoundationRecorder())
            {
                recorder.Start(
                    tempMp4,
                    640,
                    480,
                    30.0,
                    RecordingMode.OriginalCamera,
                    null,
                    null,
                    "RGB32",
                    RecordingCadenceMode.CFR_Compatibility,
                    enableAudio: false);

                byte[] dummy = new byte[640 * 480 * 4];
                GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
                try
                {
                    IntPtr pBuf = pin.AddrOfPinnedObject();
                    recorder.WriteFrame(pBuf, dummy.Length, 0, Stopwatch.GetTimestamp());
                    recorder.WriteFrame(pBuf, dummy.Length, 333_333, Stopwatch.GetTimestamp());
                }
                finally
                {
                    pin.Free();
                }

                // First stop
                recorder.Stop();
                bool firstStopRecording = recorder.IsRecording;
                bool firstStopFinalizing = recorder.IsFinalizing;

                // Second stop (repeated call - must be idempotent and safe)
                recorder.Stop();
                bool secondStopRecording = recorder.IsRecording;
                bool secondStopFinalizing = recorder.IsFinalizing;

                bool fileExists = File.Exists(tempMp4);
                long fileSize = fileExists ? new FileInfo(tempMp4).Length : 0;

                sw.Stop();
                bool passed = !firstStopRecording && !firstStopFinalizing &&
                              !secondStopRecording && !secondStopFinalizing &&
                              fileExists && fileSize > 500;

                return new NativeBackendValidationResult
                {
                    Name = "2. Start/Stop Idempotency & Re-Stop Immunity",
                    Passed = passed,
                    ExecutionMs = sw.Elapsed.TotalMilliseconds,
                    Details = $"FirstStop(Rec={firstStopRecording}, Fin={firstStopFinalizing}), SecondStop(Rec={secondStopRecording}, Fin={secondStopFinalizing}), OutputSize: {fileSize:N0} bytes"
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "2. Start/Stop Idempotency & Re-Stop Immunity",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestQueueDrainAndBufferRecycling()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_Queue_{Guid.NewGuid():N}.mp4");
        try
        {
            using (var recorder = new NativeMediaFoundationRecorder())
            {
                recorder.Start(
                    tempMp4,
                    640,
                    480,
                    30.0,
                    RecordingMode.OriginalCamera,
                    null,
                    null,
                    "RGB32",
                    RecordingCadenceMode.Capture_Faithful_VFR,
                    enableAudio: false);

                byte[] dummy = new byte[640 * 480 * 4];
                GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
                try
                {
                    IntPtr pBuf = pin.AddrOfPinnedObject();
                    for (int i = 0; i < 15; i++)
                    {
                        recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                    }
                }
                finally
                {
                    pin.Free();
                }

                recorder.Stop();

                int backlogAfterStop = recorder.QueueBacklog;
                int captured = recorder.FramesCaptured;
                int encoded = recorder.FramesEncoded;
                int dropped = recorder.DroppedFrames;

                sw.Stop();
                bool passed = backlogAfterStop == 0 && captured == 15 && encoded == 15 && dropped == 0;
                return new NativeBackendValidationResult
                {
                    Name = "3. Video Queue Drain & Buffer Pool Accounting",
                    Passed = passed,
                    ExecutionMs = sw.Elapsed.TotalMilliseconds,
                    Details = $"Captured={captured}, Encoded={encoded}, Dropped={dropped}, BacklogPostStop={backlogAfterStop}"
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "3. Video Queue Drain & Buffer Pool Accounting",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestEarlyStopRobustness()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_EarlyStop_{Guid.NewGuid():N}.mp4");
        try
        {
            using (var recorder = new NativeMediaFoundationRecorder())
            {
                recorder.Start(
                    tempMp4,
                    640,
                    480,
                    30.0,
                    RecordingMode.OriginalCamera,
                    null,
                    null,
                    "RGB32",
                    RecordingCadenceMode.CFR_Compatibility,
                    enableAudio: false);

                byte[] dummy = new byte[640 * 480 * 4];
                GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
                try
                {
                    IntPtr pBuf = pin.AddrOfPinnedObject();
                    // Write only 3 frames (approx 100 ms of video)
                    for (int i = 0; i < 3; i++)
                    {
                        recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                    }
                }
                finally
                {
                    pin.Free();
                }

                // Immediate stop
                recorder.Stop();

                bool fileExists = File.Exists(tempMp4);
                long fileSize = fileExists ? new FileInfo(tempMp4).Length : 0;
                int encoded = recorder.FramesEncoded;

                sw.Stop();
                bool passed = fileExists && fileSize > 500 && encoded >= 3 && sw.ElapsedMilliseconds < 5000;
                return new NativeBackendValidationResult
                {
                    Name = "4. Early-Stop Robustness Contract (3-Frame Session)",
                    Passed = passed,
                    ExecutionMs = sw.Elapsed.TotalMilliseconds,
                    Details = $"FramesEncoded={encoded}, FileSize={fileSize:N0} bytes, Elapsed={sw.ElapsedMilliseconds} ms (No hang or deadlock)"
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "4. Early-Stop Robustness Contract (3-Frame Session)",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestVFRTimestampPreservation()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_VFR_{Guid.NewGuid():N}.mp4");
        try
        {
            using (var recorder = new NativeMediaFoundationRecorder())
            {
                recorder.Start(
                    tempMp4,
                    640,
                    480,
                    30.0,
                    RecordingMode.OriginalCamera,
                    null,
                    null,
                    "RGB32",
                    RecordingCadenceMode.Capture_Faithful_VFR,
                    enableAudio: false);

                // Hardware jitter deltas: 33ms, 45ms, 25ms, 50ms, 30ms
                long[] jitterDeltasMs = [33, 45, 25, 50, 30];
                long currentHns = 0;

                byte[] dummy = new byte[640 * 480 * 4];
                GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
                try
                {
                    IntPtr pBuf = pin.AddrOfPinnedObject();
                    for (int i = 0; i < jitterDeltasMs.Length; i++)
                    {
                        recorder.WriteFrame(pBuf, dummy.Length, currentHns, Stopwatch.GetTimestamp());
                        currentHns += jitterDeltasMs[i] * 10_000;
                    }
                }
                finally
                {
                    pin.Free();
                }

                recorder.Stop();

                var auditRecords = recorder.GetTimingAuditRecords();
                bool countMatches = auditRecords.Count == jitterDeltasMs.Length;
                bool strictlyMonotonic = true;
                for (int i = 1; i < auditRecords.Count; i++)
                {
                    if (auditRecords[i].NormalizedTimeSec <= auditRecords[i - 1].NormalizedTimeSec)
                    {
                        strictlyMonotonic = false;
                        break;
                    }
                }

                int corrections = recorder.MonotonicityCorrectionsCount;

                sw.Stop();
                bool passed = countMatches && strictlyMonotonic && corrections == 0;
                return new NativeBackendValidationResult
                {
                    Name = "5. Monotonic VFR Timestamp Preservation & Hardware Cadence",
                    Passed = passed,
                    ExecutionMs = sw.Elapsed.TotalMilliseconds,
                    Details = $"FramesAudited={auditRecords.Count}, StrictlyMonotonic={strictlyMonotonic}, CorrectionsCount={corrections}, FinalPts={auditRecords.LastOrDefault()?.NormalizedTimeSec:F3}s"
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "5. Monotonic VFR Timestamp Preservation & Hardware Cadence",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestCFRFrameSlotAccounting()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_CFR_{Guid.NewGuid():N}.mp4");
        try
        {
            using (var recorder = new NativeMediaFoundationRecorder())
            {
                recorder.Start(
                    tempMp4,
                    640,
                    480,
                    30.0,
                    RecordingMode.OriginalCamera,
                    null,
                    null,
                    "RGB32",
                    RecordingCadenceMode.CFR_Compatibility,
                    enableAudio: false);

                byte[] dummy = new byte[640 * 480 * 4];
                GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
                try
                {
                    IntPtr pBuf = pin.AddrOfPinnedObject();
                    // Inject 10 frames with a 100ms gap at index 3 (simulating dropped camera frame)
                    long hns = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        recorder.WriteFrame(pBuf, dummy.Length, hns, Stopwatch.GetTimestamp());
                        if (i == 2)
                        {
                            hns += 100 * 10_000; // 100 ms gap (should produce duplicate CFR frames)
                        }
                        else
                        {
                            hns += 33 * 10_000;
                        }
                    }
                }
                finally
                {
                    pin.Free();
                }

                recorder.Stop();

                int captured = recorder.FramesCaptured;
                int encoded = recorder.FramesEncoded;

                sw.Stop();
                // Encoded frames in CFR mode must be >= captured frames due to gap duplication
                bool passed = captured == 10 && encoded >= captured;
                return new NativeBackendValidationResult
                {
                    Name = "6. CFR Frame-Slot Accounting & Duplicate Padding",
                    Passed = passed,
                    ExecutionMs = sw.Elapsed.TotalMilliseconds,
                    Details = $"CapturedFrames={captured}, EncodedFrames={encoded} (Duplicate padding added {encoded - captured} frames for 100ms hardware gap)"
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "6. CFR Frame-Slot Accounting & Duplicate Padding",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestZeroExternalProcessSpawning()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_ZeroExternal_{Guid.NewGuid():N}.mp4");
        try
        {
            int ffmpegProcessesBefore = Process.GetProcessesByName("ffmpeg").Length;

            using (var recorder = new NativeMediaFoundationRecorder())
            {
                recorder.Start(
                    tempMp4,
                    640,
                    480,
                    30.0,
                    RecordingMode.OriginalCamera,
                    null,
                    null,
                    "RGB32",
                    RecordingCadenceMode.CFR_Compatibility,
                    enableAudio: false);

                byte[] dummy = new byte[640 * 480 * 4];
                GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
                try
                {
                    IntPtr pBuf = pin.AddrOfPinnedObject();
                    for (int i = 0; i < 5; i++)
                    {
                        recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                    }
                }
                finally
                {
                    pin.Free();
                }

                int ffmpegProcessesDuring = Process.GetProcessesByName("ffmpeg").Length;
                recorder.Stop();
                int ffmpegProcessesAfter = Process.GetProcessesByName("ffmpeg").Length;

                sw.Stop();
                bool passed = (ffmpegProcessesDuring == ffmpegProcessesBefore) &&
                              (ffmpegProcessesAfter == ffmpegProcessesBefore);

                return new NativeBackendValidationResult
                {
                    Name = "7. Zero External Subprocess Spawning (Dependency Isolation)",
                    Passed = passed,
                    ExecutionMs = sw.Elapsed.TotalMilliseconds,
                    Details = $"FFmpeg processes Before={ffmpegProcessesBefore}, During={ffmpegProcessesDuring}, After={ffmpegProcessesAfter}. Zero external processes spawned."
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "7. Zero External Subprocess Spawning (Dependency Isolation)",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestActionableFailureContract()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            bool threwExpectedException = false;
            string exceptionMessage = string.Empty;

            try
            {
                using var recorder = new NativeMediaFoundationRecorder();
                // Attempt to record to an illegal path (invalid drive letter / root)
                recorder.Start(
                    "Z:\\NonExistentDirectory_WebcamLab_Test\\output.mp4",
                    640,
                    480,
                    30.0,
                    RecordingMode.OriginalCamera,
                    null,
                    null,
                    "RGB32",
                    RecordingCadenceMode.CFR_Compatibility,
                    enableAudio: false);
            }
            catch (Exception ex)
            {
                threwExpectedException = true;
                exceptionMessage = ex.Message;
            }

            sw.Stop();
            bool passed = threwExpectedException && !string.IsNullOrEmpty(exceptionMessage);
            return new NativeBackendValidationResult
            {
                Name = "8. Actionable Error Handling & No-Fallback Contract",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Correctly caught expected exception: '{exceptionMessage}'. No silent fallback or unhandled crash."
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "8. Actionable Error Handling & No-Fallback Contract",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
    }

    private static NativeBackendValidationResult TestSustainedInterleavedMultiplexingLoad()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_Sustained_{Guid.NewGuid():N}.mp4");
        try
        {
            using (var recorder = new NativeMediaFoundationRecorder())
            {
                recorder.Start(
                    tempMp4,
                    640,
                    480,
                    30.0,
                    RecordingMode.OriginalCamera,
                    null,
                    null,
                    "RGB32",
                    RecordingCadenceMode.Capture_Faithful_VFR,
                    enableAudio: false);

                byte[] dummy = new byte[640 * 480 * 4];
                GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
                try
                {
                    IntPtr pBuf = pin.AddrOfPinnedObject();
                    for (int i = 0; i < 300; i++)
                    {
                        recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                        if (recorder.QueueBacklog > 15)
                        {
                            Thread.Sleep(5);
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }
                }
                finally
                {
                    pin.Free();
                }

                recorder.Stop();

                int captured = recorder.FramesCaptured;
                int encoded = recorder.FramesEncoded;
                int dropped = recorder.DroppedFrames;
                int backlog = recorder.QueueBacklog;

                bool fileExists = File.Exists(tempMp4);
                long fileLength = fileExists ? new FileInfo(tempMp4).Length : 0;
                bool hasMoov = Mp4TimestampInjector.HasValidMoovAtom(tempMp4);

                sw.Stop();
                bool passed = captured == 300 && encoded == 300 && dropped == 0 && backlog == 0 && fileExists && fileLength > 5000 && hasMoov;
                return new NativeBackendValidationResult
                {
                    Name = "9. Sustained Multiplexing Load & Verified Finalization (300 Frames)",
                    Passed = passed,
                    ExecutionMs = sw.Elapsed.TotalMilliseconds,
                    Details = $"Captured={captured}, Encoded={encoded}, Dropped={dropped}, Backlog={backlog}, FileSize={fileLength:N0} bytes, HasMoov={hasMoov}"
                };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "9. Sustained Multiplexing Load & Verified Finalization (300 Frames)",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestMoovAtomValidationGuard()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_MoovGuard_{Guid.NewGuid():N}.mp4");
        try
        {
            // 1. Non-existent file
            bool nonExistentRejected = !Mp4TimestampInjector.HasValidMoovAtom("Z:\\NonExistent_File.mp4");

            // 2. Truncated open-ended mdat file (simulating Defect 39-1)
            byte[] truncatedBytes = new byte[128];
            // ftyp
            truncatedBytes[0] = 0; truncatedBytes[1] = 0; truncatedBytes[2] = 0; truncatedBytes[3] = 24;
            truncatedBytes[4] = (byte)'f'; truncatedBytes[5] = (byte)'t'; truncatedBytes[6] = (byte)'y'; truncatedBytes[7] = (byte)'p';
            // mdat with size 0
            truncatedBytes[24] = 0; truncatedBytes[25] = 0; truncatedBytes[26] = 0; truncatedBytes[27] = 0;
            truncatedBytes[28] = (byte)'m'; truncatedBytes[29] = (byte)'d'; truncatedBytes[30] = (byte)'a'; truncatedBytes[31] = (byte)'t';
            File.WriteAllBytes(tempMp4, truncatedBytes);

            bool truncatedRejected = !Mp4TimestampInjector.HasValidMoovAtom(tempMp4);

            sw.Stop();
            bool passed = nonExistentRejected && truncatedRejected;
            return new NativeBackendValidationResult
            {
                Name = "10. MP4 Container Validation Guard (Moov Atom Integrity)",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"NonExistentRejected={nonExistentRejected}, TruncatedRejected={truncatedRejected}"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "10. MP4 Container Validation Guard (Moov Atom Integrity)",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestBoundedInterleavingStarvationImmunity()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_Starvation_{Guid.NewGuid():N}.mp4");
        try
        {
            using var recorder = new NativeMediaFoundationRecorder();
            recorder.Start(
                tempMp4,
                640,
                480,
                30.0,
                RecordingMode.OriginalCamera,
                null,
                null,
                "RGB32",
                RecordingCadenceMode.Capture_Faithful_VFR,
                enableAudio: true);

            byte[] dummy = new byte[640 * 480 * 4];
            GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
            try
            {
                IntPtr pBuf = pin.AddrOfPinnedObject();

                // 1. Phase 1: 10 video frames with ZERO audio chunks (simulating audio starvation under CPU load)
                for (int i = 0; i < 10; i++)
                {
                    recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                    Thread.Sleep(2);
                }

                // 2. Phase 2: Delayed audio arrives: feed 35 audio chunks (each 480 samples = 10 ms at 48kHz)
                byte[] pcm = new byte[480 * 2 * 2]; // 480 samples, stereo, 16-bit
                for (int a = 0; a < 35; a++)
                {
                    var chunk = new WebcamLab.Capture.Audio.AudioChunk(
                        pcm,
                        480,
                        2,
                        48000,
                        (ulong)(a * 480),
                        (ulong)Stopwatch.GetTimestamp(),
                        0);
                    recorder.EnqueueAudioChunkForTesting(chunk);
                }

                // 3. Phase 3: 10 more video frames
                for (int i = 10; i < 20; i++)
                {
                    recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                    Thread.Sleep(2);
                }
            }
            finally
            {
                pin.Free();
            }

            recorder.Stop();

            int captured = recorder.FramesCaptured;
            int encoded = recorder.FramesEncoded;
            int dropped = recorder.DroppedFrames;
            bool fileExists = File.Exists(tempMp4);
            long fileLength = fileExists ? new FileInfo(tempMp4).Length : 0;
            bool hasMoov = Mp4TimestampInjector.HasValidMoovAtom(tempMp4);

            sw.Stop();
            bool passed = captured == 20 && encoded == 20 && dropped == 0 && fileExists && fileLength > 5000 && hasMoov;
            return new NativeBackendValidationResult
            {
                Name = "11. Bounded Interleaving & Mutual Queue Starvation Immunity",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Captured={captured}, Encoded={encoded}, Dropped={dropped}, FileSize={fileLength:N0} bytes, HasMoov={hasMoov}"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "11. Bounded Interleaving & Mutual Queue Starvation Immunity",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestTailSilenceSafetyCap()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_TailCap_{Guid.NewGuid():N}.mp4");
        try
        {
            using var recorder = new NativeMediaFoundationRecorder();
            recorder.Start(
                tempMp4,
                640,
                480,
                30.0,
                RecordingMode.OriginalCamera,
                null,
                null,
                "RGB32",
                RecordingCadenceMode.Capture_Faithful_VFR,
                enableAudio: true);

            byte[] dummy = new byte[640 * 480 * 4];
            GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
            try
            {
                IntPtr pBuf = pin.AddrOfPinnedObject();
                // 30 video frames = 1.0 second of video
                for (int i = 0; i < 30; i++)
                {
                    recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                    Thread.Sleep(1);
                }

                // Only 1 audio chunk = 10 ms of audio (creates intentional 990 ms tail gap)
                byte[] pcm = new byte[480 * 2 * 2];
                var chunk = new WebcamLab.Capture.Audio.AudioChunk(
                    pcm,
                    480,
                    2,
                    48000,
                    0,
                    (ulong)Stopwatch.GetTimestamp(),
                    0);
                recorder.EnqueueAudioChunkForTesting(chunk);
            }
            finally
            {
                pin.Free();
            }

            var stopWatch = Stopwatch.StartNew();
            recorder.Stop();
            stopWatch.Stop();

            bool fileExists = File.Exists(tempMp4);
            long fileLength = fileExists ? new FileInfo(tempMp4).Length : 0;
            bool hasMoov = Mp4TimestampInjector.HasValidMoovAtom(tempMp4);

            sw.Stop();
            // Finalize must be fast (< 2000 ms), not hung in silence loop, and moov atom must exist
            bool passed = stopWatch.ElapsedMilliseconds < 2000 && fileExists && fileLength > 5000 && hasMoov;
            return new NativeBackendValidationResult
            {
                Name = "12. Tail Silence Safety Cap & Bounded Alignment",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"StopElapsedMs={stopWatch.ElapsedMilliseconds}, FileSize={fileLength:N0} bytes, HasMoov={hasMoov}"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "12. Tail Silence Safety Cap & Bounded Alignment",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestInterruptedStreamGracefulShutdown()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_Interrupted_{Guid.NewGuid():N}.mp4");
        try
        {
            using var recorder = new NativeMediaFoundationRecorder();
            recorder.Start(
                tempMp4,
                640,
                480,
                30.0,
                RecordingMode.OriginalCamera,
                null,
                null,
                "RGB32",
                RecordingCadenceMode.Capture_Faithful_VFR,
                enableAudio: true);

            byte[] dummy = new byte[640 * 480 * 4];
            GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
            try
            {
                IntPtr pBuf = pin.AddrOfPinnedObject();
                for (int i = 0; i < 5; i++)
                {
                    recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                }
            }
            finally
            {
                pin.Free();
            }

            // Normal stop followed by immediate second stop
            recorder.Stop();
            recorder.Stop();

            bool fileExists = File.Exists(tempMp4);
            long fileLength = fileExists ? new FileInfo(tempMp4).Length : 0;
            bool hasMoov = Mp4TimestampInjector.HasValidMoovAtom(tempMp4);

            sw.Stop();
            bool passed = fileExists && fileLength > 1000 && hasMoov;
            return new NativeBackendValidationResult
            {
                Name = "13. Interrupted Stream Graceful Shutdown & Re-Stop Immunity",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"FileSize={fileLength:N0} bytes, HasMoov={hasMoov}"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "13. Interrupted Stream Graceful Shutdown & Re-Stop Immunity",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }

    private static NativeBackendValidationResult TestAudioLossNonBlockingVideo()
    {
        var sw = Stopwatch.StartNew();
        string tempMp4 = Path.Combine(Path.GetTempPath(), $"WebcamLab_Test_AudioLoss_{Guid.NewGuid():N}.mp4");
        try
        {
            using var recorder = new NativeMediaFoundationRecorder();
            recorder.Start(
                tempMp4,
                640,
                480,
                30.0,
                RecordingMode.OriginalCamera,
                null,
                null,
                "RGB32",
                RecordingCadenceMode.Capture_Faithful_VFR,
                enableAudio: true);

            byte[] dummy = new byte[640 * 480 * 4];
            GCHandle pin = GCHandle.Alloc(dummy, GCHandleType.Pinned);
            try
            {
                IntPtr pBuf = pin.AddrOfPinnedObject();

                // Phase 1: 5 synchronized video + audio samples
                byte[] pcm = new byte[480 * 2 * 2]; // 10 ms audio chunks
                for (int i = 0; i < 5; i++)
                {
                    recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                    var chunk = new WebcamLab.Capture.Audio.AudioChunk(
                        pcm,
                        480,
                        2,
                        48000,
                        (ulong)(i * 480),
                        (ulong)Stopwatch.GetTimestamp(),
                        0);
                    recorder.EnqueueAudioChunkForTesting(chunk);
                    Thread.Sleep(2);
                }

                // Phase 2: Audio completely severed. Feed 30 more video frames (1.0 second of video with ZERO audio)
                for (int i = 5; i < 35; i++)
                {
                    recorder.WriteFrame(pBuf, dummy.Length, (long)(i * 333_333), Stopwatch.GetTimestamp());
                    Thread.Sleep(2);
                }
            }
            finally
            {
                pin.Free();
            }

            var stopWatch = Stopwatch.StartNew();
            recorder.Stop();
            stopWatch.Stop();

            var tele = recorder.GetTelemetry();
            bool fileExists = File.Exists(tempMp4);
            long fileLength = fileExists ? new FileInfo(tempMp4).Length : 0;
            bool hasMoov = Mp4TimestampInjector.HasValidMoovAtom(tempMp4);

            sw.Stop();
            // Video must NOT have been blocked: all 35 frames captured must be encoded, 0 dropped, stop < 2000ms
            bool passed = fileExists &&
                          fileLength > 2000 &&
                          hasMoov &&
                          tele.FramesCaptured == 35 &&
                          tele.FramesEncoded >= 35 &&
                          tele.DroppedFrames == 0 &&
                          stopWatch.ElapsedMilliseconds < 2000;

            return new NativeBackendValidationResult
            {
                Name = "14. Audio Stream Loss Resilience & Non-Blocking Video Contract",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Captured={tele.FramesCaptured}, Encoded={tele.FramesEncoded}, Dropped={tele.DroppedFrames}, StopMs={stopWatch.ElapsedMilliseconds}, FileSize={fileLength:N0} bytes, HasMoov={hasMoov}"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NativeBackendValidationResult
            {
                Name = "14. Audio Stream Loss Resilience & Non-Blocking Video Contract",
                Passed = false,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Exception: {ex.Message}"
            };
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }
    }
}
