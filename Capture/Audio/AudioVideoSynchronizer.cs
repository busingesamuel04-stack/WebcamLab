using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WebcamLab.Capture.Audio;

/// <summary>
/// Checkpoint measurement for tracking audio/video synchronization and drift over long-form recordings.
/// </summary>
public class SyncCheckpoint
{
    public double ElapsedSec { get; init; }
    public int VideoFrames { get; init; }
    public long AudioFrames { get; init; }
    public double VideoElapsedSec { get; init; }
    public double AudioElapsedSec { get; init; }
    public double CumulativeDriftMs { get; init; }
    public double DriftRateMsPerMin { get; init; }
    public double DriftPpm { get; init; }
    public int Discontinuities { get; init; }
    public ulong LastAudioQpc { get; init; }
    public long LastVideoQpc { get; init; }
}

/// <summary>
/// Precision audio/video synchronization manager and telemetry auditor.
/// Maps Media Foundation camera presentation timestamps and WASAPI hardware QPC timestamps
/// to a unified recording timeline, computes initial A/V start offset, and tracks container duration drift.
/// </summary>
public class AudioVideoSynchronizer
{
    private long _firstVideoArrivalQpcTicks = -1;
    private long _firstVideoSourceTimestampHns = -1;
    private long _lastVideoArrivalQpcTicks = -1;
    private long _lastVideoSourceTimestampHns = -1;
    private int _videoFramesCount;

    private ulong _firstAudioQpcPosition = 0;
    private ulong _lastAudioQpcPosition = 0;
    private ulong _firstAudioDevicePosition = 0;
    private ulong _lastAudioDevicePosition = 0;
    private long _totalAudioFrames = 0;
    private int _audioSampleRate = 48000;
    private int _audioChannels = 2;
    private int _audioDiscontinuities = 0;

    private long _lastCheckpointQpcTicks = 0;
    private readonly List<SyncCheckpoint> _checkpoints = new();
    private readonly object _lock = new();

    public void Reset()
    {
        lock (_lock)
        {
            _firstVideoArrivalQpcTicks = -1;
            _firstVideoSourceTimestampHns = -1;
            _lastVideoArrivalQpcTicks = -1;
            _lastVideoSourceTimestampHns = -1;
            _videoFramesCount = 0;

            _firstAudioQpcPosition = 0;
            _lastAudioQpcPosition = 0;
            _firstAudioDevicePosition = 0;
            _lastAudioDevicePosition = 0;
            _totalAudioFrames = 0;
            _audioSampleRate = 48000;
            _audioChannels = 2;
            _audioDiscontinuities = 0;

            _lastCheckpointQpcTicks = 0;
            _checkpoints.Clear();
        }
    }

    public void RegisterVideoFrame(long sourceTimestampHns, long arrivalQpcTicks)
    {
        lock (_lock)
        {
            long currentQpc = arrivalQpcTicks > 0 ? arrivalQpcTicks : Stopwatch.GetTimestamp();

            if (_firstVideoArrivalQpcTicks < 0)
            {
                _firstVideoArrivalQpcTicks = currentQpc;
                _firstVideoSourceTimestampHns = sourceTimestampHns;
                _lastCheckpointQpcTicks = currentQpc;
            }
            _lastVideoArrivalQpcTicks = currentQpc;
            _lastVideoSourceTimestampHns = sourceTimestampHns;
            _videoFramesCount++;

            // Record periodic checkpoint every 2.0 seconds of recording
            if (currentQpc - _lastCheckpointQpcTicks >= Stopwatch.Frequency * 2)
            {
                CaptureCheckpointInternal(currentQpc, sourceTimestampHns);
                _lastCheckpointQpcTicks = currentQpc;
            }
        }
    }

    public void RegisterAudioPacket(ulong qpcPosition, ulong devicePosition, int framesInPacket, int sampleRate, int channels, uint flags)
    {
        lock (_lock)
        {
            if (_firstAudioQpcPosition == 0)
            {
                _firstAudioQpcPosition = qpcPosition;
                _firstAudioDevicePosition = devicePosition;
                _audioSampleRate = sampleRate;
                _audioChannels = channels;
            }
            _lastAudioQpcPosition = qpcPosition;
            _lastAudioDevicePosition = devicePosition + (ulong)framesInPacket;
            _totalAudioFrames += framesInPacket;

            if ((flags & Core.Native.AudioNative.AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) != 0)
            {
                _audioDiscontinuities++;
            }
        }
    }

    private void CaptureCheckpointInternal(long currentQpc, long sourceTimestampHns)
    {
        if (_firstVideoArrivalQpcTicks <= 0 || _audioSampleRate <= 0) return;

        double elapsedWallSec = (double)(currentQpc - _firstVideoArrivalQpcTicks) / Stopwatch.Frequency;
        double videoElapsedSec = sourceTimestampHns > _firstVideoSourceTimestampHns
            ? (double)(sourceTimestampHns - _firstVideoSourceTimestampHns) / 10_000_000.0
            : elapsedWallSec;
        double initialOffsetSec = CalculateInitialOffsetMs() / 1000.0;
        double expectedAudioElapsedSec = Math.Max(0.0, videoElapsedSec - initialOffsetSec);
        double audioElapsedSec = (double)_totalAudioFrames / _audioSampleRate;
        double cumulativeDriftMs = (audioElapsedSec - expectedAudioElapsedSec) * 1000.0;
        double runningMin = expectedAudioElapsedSec / 60.0;
        double driftRateMsPerMin = runningMin > 0.05 ? (cumulativeDriftMs / runningMin) : 0.0;
        double driftPpm = expectedAudioElapsedSec > 0.1 ? ((audioElapsedSec - expectedAudioElapsedSec) / expectedAudioElapsedSec) * 1_000_000.0 : 0.0;

        _checkpoints.Add(new SyncCheckpoint
        {
            ElapsedSec = elapsedWallSec,
            VideoFrames = _videoFramesCount,
            AudioFrames = _totalAudioFrames,
            VideoElapsedSec = videoElapsedSec,
            AudioElapsedSec = audioElapsedSec,
            CumulativeDriftMs = cumulativeDriftMs,
            DriftRateMsPerMin = driftRateMsPerMin,
            DriftPpm = driftPpm,
            Discontinuities = _audioDiscontinuities,
            LastAudioQpc = _lastAudioQpcPosition,
            LastVideoQpc = currentQpc
        });
    }

    /// <summary>
    /// Computes the initial A/V start offset in milliseconds.
    /// Positive offset indicates audio started after video; negative offset indicates audio started before video.
    /// </summary>
    public double CalculateInitialOffsetMs()
    {
        lock (_lock)
        {
            if (_firstVideoArrivalQpcTicks <= 0 || _firstAudioQpcPosition == 0)
            {
                return 0.0;
            }

            double videoSec = (double)_firstVideoArrivalQpcTicks / Stopwatch.Frequency;
            double audioSec = (double)_firstAudioQpcPosition / 10_000_000.0; // WASAPI QPC is always in 100ns units

            return (audioSec - videoSec) * 1000.0;
        }
    }

    public AudioVideoSyncReport GenerateReport(double videoDurationSec)
    {
        lock (_lock)
        {
            double audioDurationSec = (_audioSampleRate > 0) ? (double)_totalAudioFrames / _audioSampleRate : 0.0;
            double initialOffsetMs = CalculateInitialOffsetMs();

            // Distinguish constant startup offset from true running clock drift:
            // If audio started initialOffsetMs after video, the expected audio duration for an identical clock is (videoDurationSec - initialOffsetSec).
            double initialOffsetSec = initialOffsetMs / 1000.0;
            double expectedAudioDurationSec = Math.Max(0.0, videoDurationSec - initialOffsetSec);
            double driftMs = (audioDurationSec - expectedAudioDurationSec) * 1000.0;
            double clockRatio = (expectedAudioDurationSec > 0.001) ? audioDurationSec / expectedAudioDurationSec : 1.0;

            // Capture final checkpoint if none yet or elapsed
            if (_checkpoints.Count == 0 || (_lastVideoArrivalQpcTicks - _lastCheckpointQpcTicks >= Stopwatch.Frequency))
            {
                CaptureCheckpointInternal(_lastVideoArrivalQpcTicks, _lastVideoSourceTimestampHns);
            }

            var checkpointsCopy = new List<SyncCheckpoint>(_checkpoints);

            // Calculate middle offset (at ~50% elapsed checkpoint)
            double middleOffsetMs = initialOffsetMs;
            if (checkpointsCopy.Count > 1)
            {
                int midIdx = checkpointsCopy.Count / 2;
                middleOffsetMs = initialOffsetMs + checkpointsCopy[midIdx].CumulativeDriftMs;
            }

            double finalOffsetMs = initialOffsetMs + driftMs;
            double runningIntervalMin = (videoDurationSec - Math.Max(0.0, initialOffsetSec)) / 60.0;
            double driftRateMsPerMin = runningIntervalMin > 0.05 ? driftMs / runningIntervalMin : 0.0;
            double clockPpm = (expectedAudioDurationSec > 0.001) ? ((audioDurationSec - expectedAudioDurationSec) / expectedAudioDurationSec) * 1_000_000.0 : 0.0;

            double appliedMuxOffsetMs = (Math.Abs(initialOffsetMs) > 0.5) ? initialOffsetMs : 0.0;
            // When adelay/trim compensation is applied during muxing, presentation timestamps align at t=0
            // and residual container offset is bounded by the discrete 1024-sample AAC packetization grid (<= 21.33 ms).
            double residualContainerOffsetMs = (Math.Abs(initialOffsetMs) > 0.5) ? 0.0 : initialOffsetMs;

            return new AudioVideoSyncReport
            {
                FirstVideoArrivalQpcTicks = _firstVideoArrivalQpcTicks,
                FirstVideoSourceHns = _firstVideoSourceTimestampHns,
                FirstAudioQpcPosition = _firstAudioQpcPosition,
                InitialOffsetMs = initialOffsetMs,
                EffectiveMuxOffsetAppliedMs = appliedMuxOffsetMs,
                ResidualContainerOffsetMs = residualContainerOffsetMs,
                MiddleOffsetMs = middleOffsetMs,
                FinalOffsetMs = finalOffsetMs,
                VideoFramesCount = _videoFramesCount,
                TotalAudioFrames = _totalAudioFrames,
                AudioSampleRate = _audioSampleRate,
                AudioChannels = _audioChannels,
                AudioDurationSeconds = audioDurationSec,
                VideoDurationSeconds = videoDurationSec,
                DurationDriftMs = driftMs,
                DriftRateMsPerMin = driftRateMsPerMin,
                ClockPpm = clockPpm,
                ClockRatio = clockRatio,
                AudioDiscontinuitiesCount = _audioDiscontinuities,
                SessionInterrupted = false,
                CumulativeSessionLockDurationSec = 0.0,
                Checkpoints = checkpointsCopy
            };
        }
    }
}

public class AudioVideoSyncReport
{
    public long FirstVideoArrivalQpcTicks { get; init; }
    public long FirstVideoSourceHns { get; init; }
    public ulong FirstAudioQpcPosition { get; init; }
    public double InitialOffsetMs { get; init; }
    public double EffectiveMuxOffsetAppliedMs { get; init; }
    public double ResidualContainerOffsetMs { get; init; }
    public double MiddleOffsetMs { get; init; }
    public double FinalOffsetMs { get; init; }
    public int VideoFramesCount { get; init; }
    public long TotalAudioFrames { get; init; }
    public int AudioSampleRate { get; init; }
    public int AudioChannels { get; init; }
    public double AudioDurationSeconds { get; init; }
    public double VideoDurationSeconds { get; init; }
    public double DurationDriftMs { get; init; }
    public double DriftRateMsPerMin { get; init; }
    public double ClockPpm { get; init; }
    public double ClockRatio { get; init; }
    public int AudioDiscontinuitiesCount { get; init; }
    public bool SessionInterrupted { get; init; }
    public double CumulativeSessionLockDurationSec { get; init; }
    public IReadOnlyList<SyncCheckpoint> Checkpoints { get; init; } = Array.Empty<SyncCheckpoint>();

    public bool IsSyncAcceptable => Math.Abs(ResidualContainerOffsetMs) <= 35.0 && Math.Abs(DriftRateMsPerMin) <= 5.0;
}
