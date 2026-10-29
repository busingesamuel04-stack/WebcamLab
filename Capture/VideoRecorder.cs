using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using WebcamLab.Capture.Audio;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Models;
using WebcamLab.Core.Session;

namespace WebcamLab.Capture;

/// <summary>
/// High-performance video recorder producing finalized H.264 MP4 videos via FFmpeg.
/// Supports separate Original and Graded recording modes, constant-frame-rate (CFR) timestamp pacing,
/// bounded asynchronous encoding worker, and automated companion metadata sidecar generation.
/// </summary>
public class VideoRecorder : IDisposable
{
    private class FrameItem
    {
        public byte[] Buffer { get; set; } = [];
        public int Length { get; set; }
        public int RepeatCount { get; set; } = 1;
        public long SourceTimestampHns { get; set; }
        public double NormalizedTimeSec { get; set; }
    }

    private Process? _ffmpegProcess;
    private Stream? _ffmpegStdin;
    private readonly object _lock = new();
    private volatile bool _isRecording;
    private DateTime _recordStartTime;
    private long _startStopwatchTick;

    // Asynchronous decoupled encoder queue & buffer pool
    private BlockingCollection<FrameItem>? _frameQueue;
    private readonly ConcurrentQueue<byte[]> _bufferPool = new();
    private Thread? _encoderThread;
    private CancellationTokenSource? _encoderCts;

    // Cadence policy & timing tracking
    private RecordingCadenceMode _cadenceMode = RecordingCadenceMode.CFR_Compatibility;
    private int _framesCaptured;
    private int _framesEncoded;
    private int _framesDropped;
    private long _lastFrameSlot;
    private long _scheduledCfrFrames;
    private readonly object _lastFrameLock = new();
    private byte[]? _lastFrameBuffer;
    private int _lastFrameLength;
    private double _targetFps;
    private double _targetIntervalSec;
    private int _frameWidth;
    private int _frameHeight;
    private string _captureFormat = "MJPG";

    // Source timestamp precision tracking (100ns units)
    private long _initialSourceTimestampHns = -1;
    private double _lastNormalizedTimeSec = 0.0;
    private long _lastSourceTimestampHns = -1;
    private double _minFrameDeltaMs = double.MaxValue;
    private double _maxFrameDeltaMs = 0.0;
    private double _totalFrameDeltaMs = 0.0;
    private int _deltaCount = 0;

    // Monotonicity tracking & frame timing audit trail
    private int _monotonicityCorrectionsCount;
    private long _finalSourceTimestampHns = -1;
    private readonly object _auditLock = new();
    private readonly List<FrameTimingAuditRecord> _timingAuditTrail = [];

    // Metadata tracking
    private RecordingMode _recordingMode = RecordingMode.OriginalCamera;
    private SoftwareLookSettings? _activeLookSettings;
    private List<PresetControlValue>? _activeHardwareSettings;
    private TimeSpan _finalDuration = TimeSpan.Zero;

    // Audio capture and synchronization (Phase 16)
    private readonly WasapiCaptureService _audioCaptureService = new();
    private readonly AudioVideoSynchronizer _avSynchronizer = new();
    private bool _isAudioEnabled;
    private AudioDeviceInfo? _activeAudioDevice;
    private string? _tempVideoPath;
    private string? _tempWavPath;
    private AudioVideoSyncReport? _lastSyncReport;

    public bool IsAudioEnabled => _isAudioEnabled;
    public WasapiCaptureService AudioService => _audioCaptureService;
    public AudioVideoSyncReport? LastSyncReport => _lastSyncReport;
    public AudioDeviceInfo? ActiveAudioDevice => _activeAudioDevice;

    private volatile bool _isFinalizing;
    public bool IsFinalizing => _isFinalizing;
    public bool GenerateSidecarForOriginalRecordings { get; set; } = false;

    public bool IsRecording => _isRecording;
    public RecordingMode ActiveRecordingMode => _recordingMode;
    public RecordingCadenceMode ActiveCadenceMode => _cadenceMode;
    public int PaddedDuplicateFrames => Math.Max(0, _framesEncoded - _framesCaptured);
    public string? CurrentFilePath { get; private set; }
    public TimeSpan Duration => _isRecording ? DateTime.UtcNow - _recordStartTime : _finalDuration;
    public int FramesCaptured => _framesCaptured;
    public int FramesEncoded => _framesEncoded;
    public int DroppedFrames => _framesDropped;
    public int QueueBacklog => _frameQueue?.Count ?? 0;
    public double MinFrameDeltaMs => _deltaCount > 0 ? _minFrameDeltaMs : 0.0;
    public double MaxFrameDeltaMs => _deltaCount > 0 ? _maxFrameDeltaMs : 0.0;
    public double MeanFrameDeltaMs => _deltaCount > 0 ? _totalFrameDeltaMs / _deltaCount : 0.0;
    public int MonotonicityCorrectionsCount => _monotonicityCorrectionsCount;
    public long InitialSourceTimestampHns => _initialSourceTimestampHns;
    public long FinalSourceTimestampHns => _finalSourceTimestampHns;
    public double SourceDurationSeconds => (_initialSourceTimestampHns >= 0 && _finalSourceTimestampHns >= _initialSourceTimestampHns)
        ? (_finalSourceTimestampHns - _initialSourceTimestampHns) / 10_000_000.0
        : 0.0;

    public RecordingTelemetry GetTelemetry()
    {
        return new RecordingTelemetry
        {
            IsRecording = _isRecording,
            IsFinalizing = _isFinalizing,
            Mode = _recordingMode,
            CadenceMode = _cadenceMode,
            CurrentFilePath = CurrentFilePath,
            Duration = Duration,
            FramesCaptured = _framesCaptured,
            FramesEncoded = _framesEncoded,
            PaddedDuplicateFrames = PaddedDuplicateFrames,
            DroppedFrames = _framesDropped,
            QueueBacklog = QueueBacklog,
            EncodingFps = Duration.TotalSeconds > 0 ? (double)_framesEncoded / Duration.TotalSeconds : 0,
            IsAudioActive = _isAudioEnabled && _audioCaptureService.IsCapturing,
            AudioPeakLevel = _audioCaptureService.PeakLevel,
            AudioPeakDb = _audioCaptureService.PeakDb,
            AudioRmsDb = _audioCaptureService.RmsDb,
            IsAudioClipping = _audioCaptureService.IsClipping,
            AudioDiscontinuitiesCount = _audioCaptureService.DiscontinuitiesCount,
            AudioVideoOffsetMs = _lastSyncReport?.InitialOffsetMs ?? 0.0
        };
    }

    public IReadOnlyList<FrameTimingAuditRecord> GetTimingAuditRecords()
    {
        lock (_auditLock)
        {
            return new List<FrameTimingAuditRecord>(_timingAuditTrail);
        }
    }

    public static string? FindFfmpegPath()
    {
        // 1. Check application directory and local tool paths
        var appDir = AppContext.BaseDirectory;
        var directCandidate = Path.Combine(appDir, "ffmpeg.exe");
        if (File.Exists(directCandidate)) return directCandidate;

        var toolsCandidate = Path.Combine(appDir, "tools", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(toolsCandidate)) return toolsCandidate;

        var binCandidate = Path.Combine(appDir, "bin", "ffmpeg.exe");
        if (File.Exists(binCandidate)) return binCandidate;

        var subCandidate = Path.Combine(appDir, "ffmpeg", "bin", "ffmpeg.exe");
        if (File.Exists(subCandidate)) return subCandidate;

        // 2. Check PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(p.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // 3. Check WinGet Gyan.FFmpeg installation directory
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetDir = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetDir))
        {
            var ffmpegDirs = Directory.GetDirectories(wingetDir, "*FFmpeg*", SearchOption.AllDirectories);
            foreach (var dir in ffmpegDirs)
            {
                var candidate = Path.Combine(dir, "bin", "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Starts video recording session with explicit recording mode, cadence policy, and configuration.
    /// Supports both CFR_Compatibility (padded 30 FPS) and Capture_Faithful_VFR (source-timestamp-preserving),
    /// with optional timestamp-synchronized WASAPI audio capture multiplexing.
    /// </summary>
    public void Start(
        string outputPath,
        int width,
        int height,
        double fps,
        RecordingMode mode = RecordingMode.OriginalCamera,
        SoftwareLookSettings? lookSettings = null,
        IReadOnlyList<PresetControlValue>? hardwareSettings = null,
        string captureFormat = "MJPG",
        RecordingCadenceMode cadenceMode = RecordingCadenceMode.CFR_Compatibility,
        bool enableAudio = true,
        AudioDeviceInfo? audioDevice = null)
    {
        lock (_lock)
        {
            if (_isFinalizing)
            {
                throw new InvalidOperationException("Cannot start recording: recorder is currently finalizing the previous session.");
            }
            if (_isRecording) return;

            // Validate LUT existence if graded recording with LUT is requested
            if (mode == RecordingMode.GradedVideo && lookSettings != null && lookSettings.LutEnabled)
            {
                if (string.IsNullOrWhiteSpace(lookSettings.LutFilePath) || !File.Exists(lookSettings.LutFilePath))
                {
                    throw new FileNotFoundException(
                        $"The configured 3D LUT file '{lookSettings.LutFileName ?? lookSettings.LutFilePath}' was not found on disk. " +
                        "Graded video recording cannot proceed with a missing LUT.",
                        lookSettings.LutFilePath ?? "");
                }
            }

            var ffmpegPath = FindFfmpegPath() ?? "ffmpeg.exe";
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _recordingMode = mode;
            _cadenceMode = cadenceMode;
            _activeLookSettings = lookSettings?.Clone();
            _activeHardwareSettings = hardwareSettings != null ? new List<PresetControlValue>(hardwareSettings) : null;
            _frameWidth = width;
            _frameHeight = height;
            _captureFormat = captureFormat;

            _isAudioEnabled = enableAudio;
            _activeAudioDevice = audioDevice;

            string ffmpegTarget = outputPath;
            if (_isAudioEnabled)
            {
                _tempVideoPath = Path.Combine(directory ?? ".", $"temp_vid_{Guid.NewGuid():N}.mp4");
                _tempWavPath = Path.Combine(directory ?? ".", $"temp_aud_{Guid.NewGuid():N}.wav");
                ffmpegTarget = _tempVideoPath;
            }
            else
            {
                _tempVideoPath = null;
                _tempWavPath = null;
            }

            // Target CFR timeline rate (default to 30 FPS standard if stream rate is uncalibrated)
            _targetFps = fps > 0 ? fps : 30.0;
            int safeFps = (int)Math.Round(_targetFps);
            _targetIntervalSec = 1.0 / _targetFps;

            // Reset source timestamp telemetry & timing audit trail
            _initialSourceTimestampHns = -1;
            _finalSourceTimestampHns = -1;
            _lastNormalizedTimeSec = 0.0;
            _lastSourceTimestampHns = -1;
            _minFrameDeltaMs = double.MaxValue;
            _maxFrameDeltaMs = 0.0;
            _totalFrameDeltaMs = 0.0;
            _deltaCount = 0;
            _monotonicityCorrectionsCount = 0;
            lock (_auditLock)
            {
                _timingAuditTrail.Clear();
            }

            // Set high-resolution multimedia timer (1ms) for microsecond-accurate pipe pacing on Windows
            Core.Native.MediaFoundationNative.TimeBeginPeriod(1);

            // Configure FFmpeg based on cadence policy:
            // - CFR Compatibility: constant input rate with duplicate frame padding for 30fps NLE compatibility.
            // - Capture-Faithful: wall-clock presentation timestamps with variable frame rate (VFR), 1ms timebase, zero duplicate padding.
            // -bf 0: Explicitly disable B-frames to guarantee Presentation Order strictly equals Decode Order (DTS == PTS everywhere, ctts omitted).
            // Omit +faststart on temporary scratch video to avoid redundant multi-gigabyte file shifting before remux
            string fastStartFlag = _isAudioEnabled ? "" : "-movflags +faststart ";
            string ffmpegArgs = (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR)
                ? $"-y -use_wallclock_as_timestamps 1 -framerate 1000 -f rawvideo -video_size {width}x{height} -pixel_format bgr32 -i - -fps_mode passthrough -c:v libx264 -preset ultrafast -bf 0 -crf 18 -pix_fmt yuv420p {fastStartFlag}\"{ffmpegTarget}\""
                : $"-y -f rawvideo -vcodec rawvideo -s {width}x{height} -pix_fmt bgr32 -r {safeFps} -i - -c:v libx264 -preset ultrafast -bf 0 -crf 18 -pix_fmt yuv420p {fastStartFlag}\"{ffmpegTarget}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false
            };

            try
            {
                _ffmpegProcess = Process.Start(psi) ?? throw new InvalidOperationException("Could not launch FFmpeg process.");
                _ffmpegStdin = _ffmpegProcess.StandardInput.BaseStream;

                // Asynchronously drain stderr to prevent OS pipe deadlock
                _ffmpegProcess.ErrorDataReceived += (s, e) => { };
                _ffmpegProcess.BeginErrorReadLine();

                _framesCaptured = 0;
                _framesEncoded = 0;
                _framesDropped = 0;
                _lastFrameSlot = 0;
                _scheduledCfrFrames = 0;
                lock (_lastFrameLock)
                {
                    _lastFrameBuffer = null;
                    _lastFrameLength = 0;
                }
                _startStopwatchTick = Stopwatch.GetTimestamp();
                _recordStartTime = DateTime.UtcNow;
                _finalDuration = TimeSpan.Zero;
                CurrentFilePath = outputPath;

                // Manage Windows system/display idle state via WorkstationSessionManager
                // Note: Per Microsoft documentation, this prevents display sleep / system idle transitions
                // but does NOT prevent explicit workstation lock (Win+L) or bypass Windows Camera Privacy.
                WorkstationSessionManager.Instance.StartRecordingSession();
                WorkstationSessionManager.RequestActiveExecutionState(out string execMsg);
                Debug.WriteLine($"[VideoRecorder.Start] {execMsg}");

                // Setup bounded asynchronous worker queue (capacity = 25 frames, ~200MB maximum memory ceiling)
                _frameQueue = new BlockingCollection<FrameItem>(new ConcurrentQueue<FrameItem>(), boundedCapacity: 25);
                _encoderCts = new CancellationTokenSource();

                _encoderThread = new Thread(EncoderWorkerLoop)
                {
                    Name = "WebcamLab.VideoEncoderWorker",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _encoderThread.Start();

                // Initialize audio synchronization and capture if enabled
                _avSynchronizer.Reset();
                if (_isAudioEnabled && !string.IsNullOrEmpty(_tempWavPath))
                {
                    _audioCaptureService.AudioChunkAvailable += OnAudioChunkCaptured;
                    try
                    {
                        _audioCaptureService.Start(audioDevice?.Id, _tempWavPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VideoRecorder] Audio start failed, falling back to video-only: {ex.Message}");
                        _audioCaptureService.AudioChunkAvailable -= OnAudioChunkCaptured;
                        _isAudioEnabled = false;
                        _tempWavPath = null;
                    }
                }

                _isRecording = true;
            }
            catch (Exception ex)
            {
                _isRecording = false;
                CleanupProcess();
                throw new InvalidOperationException($"Failed to start video recording via FFmpeg ({ffmpegPath}): {ex.Message}", ex);
            }
        }
    }

    private void OnAudioChunkCaptured(AudioChunk chunk)
    {
        _avSynchronizer.RegisterAudioPacket(
            chunk.QPCPosition,
            chunk.DevicePosition,
            chunk.SampleCount,
            chunk.SampleRate,
            chunk.Channels,
            chunk.Flags);
    }

    /// <summary>
    /// Submits a decoded frame for encoding.
    /// In CFR Compatibility mode: Uses wall-clock slot calculation to insert duplicate padding frames if capture FPS drops.
    /// In Capture-Faithful mode: Preserves raw source timestamps with zero duplicate padding (repeatCount = 1).
    /// </summary>
    public unsafe void WriteFrame(IntPtr bgr32Ptr, int bufferSize, long sourceTimestampHns = 0, long arrivalQpcTicks = 0)
    {
        if (!_isRecording || _frameQueue == null || _frameQueue.IsAddingCompleted || bgr32Ptr == IntPtr.Zero)
        {
            return;
        }

        Interlocked.Increment(ref _framesCaptured);

        if (_isAudioEnabled)
        {
            _avSynchronizer.RegisterVideoFrame(sourceTimestampHns, arrivalQpcTicks);
        }

        // Rent buffer from pool or allocate if pool empty
        if (!_bufferPool.TryDequeue(out var buffer) || buffer.Length != bufferSize)
        {
            buffer = new byte[bufferSize];
        }

        // Fast unmanaged copy off the capture thread
        Marshal.Copy(bgr32Ptr, buffer, 0, bufferSize);

        int repeatCount = 1;
        double normalizedTimeSec = 0.0;
        double rawSec = 0.0;
        bool monotonicityCorrected = false;
        double correctionDeltaMs = 0.0;
        double interFrameDeltaMs = 0.0;

        if (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR)
        {
            // Strict 1:1 Capture-Faithful mode: exactly 1 encoded frame per captured frame. ZERO padding duplicates!
            repeatCount = 1;

            // Track genuine source inter-frame deltas (100ns units -> ms)
            if (_lastSourceTimestampHns > 0 && sourceTimestampHns > _lastSourceTimestampHns)
            {
                double deltaMs = (sourceTimestampHns - _lastSourceTimestampHns) / 10000.0;
                interFrameDeltaMs = deltaMs;
                if (deltaMs > 0 && deltaMs < 5000)
                {
                    if (deltaMs < _minFrameDeltaMs) _minFrameDeltaMs = deltaMs;
                    if (deltaMs > _maxFrameDeltaMs) _maxFrameDeltaMs = deltaMs;
                    _totalFrameDeltaMs += deltaMs;
                    _deltaCount++;
                }
            }
            _lastSourceTimestampHns = sourceTimestampHns;

            // Normalize timestamp relative to start of recording (t0 = 0)
            if (_initialSourceTimestampHns < 0)
            {
                _initialSourceTimestampHns = sourceTimestampHns > 0 ? sourceTimestampHns : 0;
                _finalSourceTimestampHns = _initialSourceTimestampHns;
                rawSec = 0.0;
                normalizedTimeSec = 0.0;
                _lastNormalizedTimeSec = 0.0;
            }
            else
            {
                _finalSourceTimestampHns = sourceTimestampHns;
                rawSec = sourceTimestampHns > _initialSourceTimestampHns
                    ? (sourceTimestampHns - _initialSourceTimestampHns) / 10_000_000.0
                    : (double)(Stopwatch.GetTimestamp() - _startStopwatchTick) / Stopwatch.Frequency;

                // Enforce strictly monotonic presentation timestamps (minimum 0.5ms step)
                if (rawSec < _lastNormalizedTimeSec + 0.0005)
                {
                    normalizedTimeSec = _lastNormalizedTimeSec + 0.0005;
                    monotonicityCorrected = true;
                    correctionDeltaMs = (normalizedTimeSec - rawSec) * 1000.0;
                    Interlocked.Increment(ref _monotonicityCorrectionsCount);
                }
                else
                {
                    normalizedTimeSec = rawSec;
                }
                _lastNormalizedTimeSec = normalizedTimeSec;
            }
        }
        else
        {
            // CFR Compatibility Mode:
            // Calculate elapsed wall-clock time since recording started
            long elapsedTicks = Stopwatch.GetTimestamp() - _startStopwatchTick;
            double elapsedSec = (double)elapsedTicks / Stopwatch.Frequency;
            long targetTotalFrames = (long)Math.Round(elapsedSec * _targetFps);

            if (_framesCaptured == 1)
            {
                repeatCount = 1;
                _scheduledCfrFrames = 1;
            }
            else
            {
                long needed = targetTotalFrames - _scheduledCfrFrames;
                // Allow up to 300 frames (10 seconds) burst to recover from camera frame stalls
                repeatCount = (int)Math.Clamp(needed, 0, 300);
                _scheduledCfrFrames += repeatCount;
            }
            _lastFrameSlot = targetTotalFrames;
            rawSec = elapsedSec;
            normalizedTimeSec = _scheduledCfrFrames * _targetIntervalSec;
            _finalSourceTimestampHns = sourceTimestampHns;

            // Cache latest frame for trailing padding at Stop()
            lock (_lastFrameLock)
            {
                if (_lastFrameBuffer == null || _lastFrameBuffer.Length != bufferSize)
                {
                    _lastFrameBuffer = new byte[bufferSize];
                }
                Buffer.BlockCopy(buffer, 0, _lastFrameBuffer, 0, bufferSize);
                _lastFrameLength = bufferSize;
            }
        }

        lock (_auditLock)
        {
            _timingAuditTrail.Add(new FrameTimingAuditRecord
            {
                FrameIndex = _framesCaptured,
                OriginalSourceTimestampHns = sourceTimestampHns,
                RawSourceTimeSec = rawSec,
                NormalizedTimeSec = normalizedTimeSec,
                MonotonicityCorrected = monotonicityCorrected,
                CorrectionDeltaMs = correctionDeltaMs,
                InterFrameDeltaMs = interFrameDeltaMs,
                RepeatCount = repeatCount
            });
        }

        var item = new FrameItem
        {
            Buffer = buffer,
            Length = bufferSize,
            RepeatCount = repeatCount,
            SourceTimestampHns = sourceTimestampHns,
            NormalizedTimeSec = normalizedTimeSec
        };

        // Non-blocking try-add: drop frame if encoder queue is backed up
        if (!_frameQueue.TryAdd(item))
        {
            Interlocked.Increment(ref _framesDropped);
            _bufferPool.Enqueue(buffer); // Recycle buffer
        }
    }

    private void EncoderWorkerLoop()
    {
        if (_frameQueue == null || _ffmpegStdin == null) return;

        try
        {
            foreach (var item in _frameQueue.GetConsumingEnumerable())
            {
                if (_ffmpegStdin == null) break;

                try
                {
                    if (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR)
                    {
                        // In Capture-Faithful mode: Pace pipe writes to match normalized source timestamp relative to t0
                        long elapsedTicks = Stopwatch.GetTimestamp() - _startStopwatchTick;
                        double elapsedSec = (double)elapsedTicks / Stopwatch.Frequency;
                        double waitSec = item.NormalizedTimeSec - elapsedSec;
                        if (waitSec > 0.001 && waitSec < 3.0)
                        {
                            int waitMs = (int)(waitSec * 1000.0);
                            if (waitMs > 1)
                            {
                                Thread.Sleep(waitMs - 1);
                            }
                            // Spin-wait for the sub-millisecond tail to ensure precise arrival at pipe
                            while (((double)(Stopwatch.GetTimestamp() - _startStopwatchTick) / Stopwatch.Frequency) < item.NormalizedTimeSec)
                            {
                                Thread.SpinWait(10);
                            }
                        }

                        // Write exactly once (zero duplicate frames) and flush to ensure immediate delivery
                        _ffmpegStdin.Write(item.Buffer, 0, item.Length);
                        _ffmpegStdin.Flush();
                        Interlocked.Increment(ref _framesEncoded);
                    }
                    else
                    {
                        // In CFR Compatibility mode: Write frame and duplicates to pad missing timeline slots
                        for (int r = 0; r < item.RepeatCount; r++)
                        {
                            _ffmpegStdin.Write(item.Buffer, 0, item.Length);
                            Interlocked.Increment(ref _framesEncoded);
                        }
                        _ffmpegStdin.Flush();
                    }
                }
                catch
                {
                    // Pipe broken or process exited
                    break;
                }
                finally
                {
                    // Recycle buffer to pool
                    _bufferPool.Enqueue(item.Buffer);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Worker canceled
        }
        catch
        {
            // Worker error
        }
    }

    /// <summary>
    /// Safely finalizes the MP4 recording, closes the FFmpeg pipe, and generates companion sidecar JSON for graded recordings.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRecording) return;
            _isFinalizing = true;
            long stopTicks = Stopwatch.GetTimestamp();
            double stopElapsedSec = (double)(stopTicks - _startStopwatchTick) / Stopwatch.Frequency;
            _finalDuration = TimeSpan.FromSeconds(stopElapsedSec);
            _isRecording = false;

            try
            {
                double effectiveAudioDurationSec = 0.0;
                if (_isAudioEnabled)
                {
                    _audioCaptureService.AudioChunkAvailable -= OnAudioChunkCaptured;
                    _audioCaptureService.Stop();

                    // Generate sync report early to obtain exact audio duration & initial offset
                    double initialEstDuration = stopElapsedSec;
                    _lastSyncReport = _avSynchronizer.GenerateReport(initialEstDuration);
                    if (_lastSyncReport != null && _lastSyncReport.AudioDurationSeconds > 0)
                    {
                        // In container, adelay extends audio presentation duration by max(0, initialOffsetMs)
                        double delayLeadSec = Math.Max(0.0, _lastSyncReport.InitialOffsetMs / 1000.0);
                        effectiveAudioDurationSec = _lastSyncReport.AudioDurationSeconds + delayLeadSec;
                    }
                }

                // Signal completion to queue and wait for worker thread to drain
                _frameQueue?.CompleteAdding();
                _encoderThread?.Join(30000);

                // In CFR Compatibility mode: pad any trailing slots up to the exact recording stop time
                if (_cadenceMode == RecordingCadenceMode.CFR_Compatibility && _ffmpegStdin != null)
                {
                    // Target timeline is aligned with effective audio presentation duration if audio is active
                    double targetTimelineSec = (_isAudioEnabled && effectiveAudioDurationSec > 0.1)
                        ? effectiveAudioDurationSec
                        : stopElapsedSec;

                    long finalTargetFrames = (long)Math.Round(targetTimelineSec * _targetFps);
                    long trailingNeeded = finalTargetFrames - _framesEncoded;
                    if (trailingNeeded > 0)
                    {
                        byte[]? padBuf = null;
                        int padLen = 0;
                        lock (_lastFrameLock)
                        {
                            if (_lastFrameBuffer != null && _lastFrameLength > 0)
                            {
                                padBuf = _lastFrameBuffer;
                                padLen = _lastFrameLength;
                            }
                        }

                        if (padBuf != null && padLen > 0)
                        {
                            int padCount = (int)Math.Min(trailingNeeded, 900); // Pad up to 30s maximum
                            for (int p = 0; p < padCount; p++)
                            {
                                _ffmpegStdin.Write(padBuf, 0, padLen);
                                Interlocked.Increment(ref _framesEncoded);
                            }
                            _ffmpegStdin.Flush();
                        }
                    }
                }

                _ffmpegStdin?.Flush();
                _ffmpegStdin?.Close();
                _ffmpegStdin = null;

                if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                {
                    // Allow up to 60 seconds for FFmpeg to flush encoder frames and write container trailer
                    _ffmpegProcess.WaitForExit(60000);
                    if (!_ffmpegProcess.HasExited)
                    {
                        _ffmpegProcess.Kill();
                    }
                }

                string videoFileToInject = (_isAudioEnabled && !string.IsNullOrEmpty(_tempVideoPath))
                    ? _tempVideoPath
                    : CurrentFilePath!;

                // In Capture-Faithful VFR mode: Inject true hardware camera timestamps into MP4 container 'stts' atom
                if (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR && File.Exists(videoFileToInject))
                {
                    var sampleTimes = new List<double>();
                    lock (_auditLock)
                    {
                        foreach (var rec in _timingAuditTrail)
                        {
                            sampleTimes.Add(rec.NormalizedTimeSec);
                        }
                    }
                    if (sampleTimes.Count > 0)
                    {
                        Mp4TimestampInjector.InjectTrueSourceTimestamps(videoFileToInject, sampleTimes);
                    }
                }

                // If audio was enabled and both temp files exist, mux them into final CurrentFilePath
                if (_isAudioEnabled && !string.IsNullOrEmpty(_tempVideoPath) && File.Exists(_tempVideoPath) &&
                    !string.IsNullOrEmpty(_tempWavPath) && File.Exists(_tempWavPath))
                {
                    double videoMediaDuration;
                    if (_cadenceMode == RecordingCadenceMode.CFR_Compatibility && _targetFps > 0)
                    {
                        videoMediaDuration = (double)_framesEncoded / _targetFps;
                    }
                    else if (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR && SourceDurationSeconds > 0)
                    {
                        videoMediaDuration = SourceDurationSeconds;
                    }
                    else
                    {
                        videoMediaDuration = Duration.TotalSeconds;
                    }

                    _lastSyncReport = _avSynchronizer.GenerateReport(videoMediaDuration);
                    MuxAudioVideo(_tempVideoPath, _tempWavPath, CurrentFilePath!, _lastSyncReport.InitialOffsetMs, _cadenceMode == RecordingCadenceMode.CFR_Compatibility);

                    try
                    {
                        if (File.Exists(CurrentFilePath) && new FileInfo(CurrentFilePath).Length > 0)
                        {
                            if (File.Exists(_tempVideoPath)) File.Delete(_tempVideoPath);
                            if (File.Exists(_tempWavPath)) File.Delete(_tempWavPath);
                        }
                        else
                        {
                            Debug.WriteLine($"[VideoRecorder.Stop] Output missing. Preserving temp files: {_tempVideoPath}, {_tempWavPath}");
                            Console.WriteLine($"[VideoRecorder.Stop] Output missing. Preserving temp files: {_tempVideoPath}, {_tempWavPath}");
                        }
                    }
                    catch { }
                }

                // If this was a graded recording, or if sidecars for original recordings are enabled, generate companion JSON metadata sidecar
                if (_recordingMode == RecordingMode.GradedVideo && !string.IsNullOrEmpty(CurrentFilePath) && File.Exists(CurrentFilePath))
                {
                    WriteSidecarV3(CurrentFilePath, isOriginalMode: false);
                }
                else if (GenerateSidecarForOriginalRecordings && !string.IsNullOrEmpty(CurrentFilePath) && File.Exists(CurrentFilePath))
                {
                    WriteSidecarV3(CurrentFilePath, isOriginalMode: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during VideoRecorder Stop: {ex.Message}");
            }
            finally
            {
                WorkstationSessionManager.Instance.StopRecordingSession();
                WorkstationSessionManager.ClearActiveExecutionState(out string clearMsg);
                Debug.WriteLine($"[VideoRecorder.Stop] {clearMsg}");
                Core.Native.MediaFoundationNative.TimeEndPeriod(1);
                CleanupProcess();
                _isFinalizing = false;
            }
        }
    }

    private static void MuxAudioVideo(string videoInputPath, string audioInputPath, string finalOutputPath, double initialOffsetMs, bool isCfrMode = false)
    {
        var ffmpegPath = FindFfmpegPath() ?? "ffmpeg.exe";

        string audioInputArg = "";
        string audioFilterArg = "";

        // If audio starts after video (initialOffsetMs > 0.5 ms):
        // Prepend silence to audio via adelay filter so audio presentation starts at t=0.000s,
        // matching the video track with zero container offset and eliminating start delay gaps.
        if (initialOffsetMs > 0.5)
        {
            int delayMs = (int)Math.Round(initialOffsetMs);
            audioInputArg = $"-i \"{audioInputPath}\" ";
            audioFilterArg = $"-af \"adelay={delayMs}|{delayMs}\" ";
        }
        else if (initialOffsetMs < -0.5)
        {
            // If audio starts before video, trim leading audio to align t=0
            double trimSec = -initialOffsetMs / 1000.0;
            audioInputArg = $"-ss {trimSec:F4} -i \"{audioInputPath}\" ";
        }
        else
        {
            audioInputArg = $"-i \"{audioInputPath}\" ";
        }

        string shortestFlag = isCfrMode ? "-shortest " : "";
        string muxArgs = $"-y -i \"{videoInputPath}\" {audioInputArg}{audioFilterArg}-c:v copy -c:a aac -b:a 192k -ar 48000 {shortestFlag}-movflags +faststart \"{finalOutputPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = muxArgs,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false
        };

        var stderrSb = new StringBuilder();
        using var proc = Process.Start(psi);
        if (proc != null)
        {
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderrSb.AppendLine(e.Data); };
            proc.BeginErrorReadLine();
            // Allow up to 3 minutes for large 15-30 minute file multiplexing and AAC encoding
            proc.WaitForExit(180000);
            if (!proc.HasExited)
            {
                proc.Kill();
            }

            if (!File.Exists(finalOutputPath) || new FileInfo(finalOutputPath).Length == 0)
            {
                Console.WriteLine($"[MuxAudioVideo ERROR] ExitCode={proc.ExitCode}\n{stderrSb}");
            }
        }
    }

    private void WriteGradedSidecar(string mp4Path) => WriteSidecarV3(mp4Path, isOriginalMode: false);

    /// <summary>
    /// Writes a complete, versioned (Schema 3.0) recording metadata sidecar next to the media file.
    /// Captures all camera telemetry, frame pacing, audio metrics, sync stats, and warnings.
    /// </summary>
    public void WriteSidecarV3(string mp4Path, bool isOriginalMode = false)
    {
        try
        {
            string sidecarPath = Path.ChangeExtension(mp4Path, ".json");
            
            // Build diagnostic warnings
            var warnings = new List<string>();
            if (_framesDropped > 0)
            {
                warnings.Add($"Dropped video frames: {_framesDropped} frames dropped during capture pipeline.");
            }
            if (_lastSyncReport?.AudioDiscontinuitiesCount > 0)
            {
                warnings.Add($"Audio packet discontinuity: {_lastSyncReport.AudioDiscontinuitiesCount} buffer discontinuity event(s) detected.");
            }
            if (WorkstationSessionManager.Instance.CumulativeLockDurationSec > 0.05)
            {
                warnings.Add($"Workstation lock detected: session locked for {WorkstationSessionManager.Instance.CumulativeLockDurationSec:F2} seconds during recording.");
            }
            if (_monotonicityCorrectionsCount > 0)
            {
                warnings.Add($"Source timestamp jitter: {_monotonicityCorrectionsCount} monotonicity timestamp correction(s) applied.");
            }

            // LUT status determination
            string lutStatus = "None";
            string? lutDetails = null;
            if (!isOriginalMode && _activeLookSettings?.LutEnabled == true)
            {
                if (!string.IsNullOrEmpty(_activeLookSettings.LutFilePath) && File.Exists(_activeLookSettings.LutFilePath))
                {
                    lutStatus = "Ready";
                    lutDetails = $"Loaded from {_activeLookSettings.LutFilePath}";
                }
                else
                {
                    lutStatus = "Missing";
                    lutDetails = $"LUT file '{_activeLookSettings.LutFileName ?? "configured.cube"}' not found on disk.";
                    warnings.Add($"Missing LUT: {lutDetails}");
                }
            }

            var metadata = new GradedVideoMetadata
            {
                SchemaVersion = RecordingSidecarV3.CurrentSchemaVersion,
                RecordingMode = isOriginalMode ? "Original" : "Graded",
                CadenceMode = _cadenceMode.ToString(),
                VideoFileName = Path.GetFileName(mp4Path),
                VideoFilePath = mp4Path,
                RecordedAtUtc = _recordStartTime,
                CompletedAtUtc = DateTime.UtcNow,
                CameraDeviceName = "Dell Monitor Integrated Webcam",
                Width = _frameWidth,
                Height = _frameHeight,
                CaptureFormat = _captureFormat,
                TargetFps = _targetFps,
                AchievedFps = Duration.TotalSeconds > 0 ? (double)_framesCaptured / Duration.TotalSeconds : 0,
                DurationSeconds = Duration.TotalSeconds,
                TotalFramesCaptured = _framesCaptured,
                TotalFramesEncoded = _framesEncoded,
                DroppedFrames = _framesDropped,
                PaddedDuplicateFrames = PaddedDuplicateFrames,
                SourceTimestampsPreserved = (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR),
                MinFrameDeltaMs = MinFrameDeltaMs,
                MaxFrameDeltaMs = MaxFrameDeltaMs,
                MeanFrameDeltaMs = MeanFrameDeltaMs,
                MonotonicityCorrectionsCount = _monotonicityCorrectionsCount,
                InitialSourceTimestampHns = _initialSourceTimestampHns,
                FinalSourceTimestampHns = _finalSourceTimestampHns,
                SourceDurationSeconds = SourceDurationSeconds,
                TimingPolicy = (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR)
                    ? "Capture-Faithful Variable Frame Rate (Source-Timestamp Preserved, Zero Padding)"
                    : "Constant Frame Rate (CFR Compatibility with Duplicate Padding)",
                FrameTimingAuditTrail = new List<FrameTimingAuditRecord>(GetTimingAuditRecords()),
                SoftwareLook = isOriginalMode
                    ? new SoftwareLookSettings { LookName = "Original Camera Passthrough", Enabled = false }
                    : (_activeLookSettings ?? new SoftwareLookSettings()),
                LutStatus = lutStatus,
                LutStatusDetails = lutDetails,
                HardwareControls = _activeHardwareSettings ?? [],

                AudioEnabled = _isAudioEnabled,
                AudioDeviceName = _activeAudioDevice?.FriendlyName,
                AudioDeviceId = _activeAudioDevice?.Id,
                AudioSampleRate = _lastSyncReport?.AudioSampleRate,
                AudioChannels = _lastSyncReport?.AudioChannels,
                AudioCodec = _isAudioEnabled ? "AAC-LC (192 kbps, 48 kHz)" : null,
                AudioDurationSeconds = _lastSyncReport?.AudioDurationSeconds,
                InitialAudioVideoOffsetMs = _lastSyncReport?.InitialOffsetMs,
                MiddleAudioVideoOffsetMs = _lastSyncReport?.MiddleOffsetMs,
                FinalAudioVideoOffsetMs = _lastSyncReport?.FinalOffsetMs,
                AudioVideoDriftMs = _lastSyncReport?.DurationDriftMs,
                DriftRateMsPerMin = _lastSyncReport?.DriftRateMsPerMin,
                ClockPpm = _lastSyncReport?.ClockPpm,
                AudioDiscontinuitiesCount = _lastSyncReport?.AudioDiscontinuitiesCount,
                SilencePacketsCount = _audioCaptureService.SilencePacketsCount,

                // Workstation session interruption telemetry (Phase 18.1)
                SessionInterrupted = WorkstationSessionManager.Instance.CumulativeLockDurationSec > 0.05,
                CumulativeSessionLockDurationSec = WorkstationSessionManager.Instance.CumulativeLockDurationSec,
                SessionEvents = new List<SessionInterruptionEvent>(WorkstationSessionManager.Instance.GetRecordingEvents()),
                Warnings = warnings
            };

            string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecarPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write recording sidecar v3: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes standalone timing audit JSON sidecar for any recording mode (Original or Graded).
    /// </summary>
    public void WriteTimingAuditSidecar(string sidecarPath)
    {
        try
        {
            var auditData = new
            {
                RecordingMode = _recordingMode.ToString(),
                CadenceMode = _cadenceMode.ToString(),
                VideoFilePath = CurrentFilePath ?? string.Empty,
                CaptureFormat = _captureFormat,
                TargetFps = _targetFps,
                DurationSeconds = Duration.TotalSeconds,
                SourceDurationSeconds = SourceDurationSeconds,
                TotalFramesCaptured = _framesCaptured,
                TotalFramesEncoded = _framesEncoded,
                DroppedFrames = _framesDropped,
                PaddedDuplicateFrames = PaddedDuplicateFrames,
                MonotonicityCorrectionsCount = _monotonicityCorrectionsCount,
                MinFrameDeltaMs = MinFrameDeltaMs,
                MaxFrameDeltaMs = MaxFrameDeltaMs,
                MeanFrameDeltaMs = MeanFrameDeltaMs,
                AudioEnabled = _isAudioEnabled,
                AudioSync = _lastSyncReport,
                TimingPolicy = (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR)
                    ? "Capture-Faithful Variable Frame Rate (Source-Timestamp Preserved, Zero Padding)"
                    : "Constant Frame Rate (CFR Compatibility with Duplicate Padding)",
                FrameTimingAuditTrail = GetTimingAuditRecords()
            };

            string json = JsonSerializer.Serialize(auditData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecarPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write timing audit sidecar: {ex.Message}");
        }
    }

    private void CleanupProcess()
    {
        try
        {
            WorkstationSessionManager.ClearActiveExecutionState(out _);
            _encoderCts?.Cancel();
            _encoderCts?.Dispose();
            _encoderCts = null;

            _frameQueue?.Dispose();
            _frameQueue = null;

            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _audioCaptureService.Dispose();
        GC.SuppressFinalize(this);
    }
}
