using System;
using System.Collections.Generic;
using WebcamLab.Capture.Audio;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Models;

namespace WebcamLab.Capture;

/// <summary>
/// Mode enumeration for selecting the recording backend engine.
/// </summary>
public enum RecordingBackendMode
{
    /// <summary>
    /// Native Windows Media Foundation IMFSinkWriter encoder (primary production default).
    /// </summary>
    MediaFoundation_Default = 0,

    /// <summary>
    /// Legacy FFmpeg subprocess pipe encoder (fallback / regression testing).
    /// </summary>
    FFmpeg_Legacy = 1,

    /// <summary>
    /// Compatibility alias for Phase 31/32 references.
    /// </summary>
    MediaFoundation_Experimental = MediaFoundation_Default
}

/// <summary>
/// Common contract for video and audio recording backends in WebcamLab.
/// Abstracts the recording pipeline so that FFmpeg and Native Media Foundation
/// backends can be selected and compared cleanly without duplicating camera acquisition.
/// </summary>
public interface IRecorderBackend : IDisposable
{
    /// <summary>
    /// Friendly display name of this recording backend.
    /// </summary>
    string BackendName { get; }

    /// <summary>
    /// Identifies the backend mode.
    /// </summary>
    RecordingBackendMode BackendMode { get; }

    bool IsRecording { get; }
    bool IsFinalizing { get; }
    string? CurrentFilePath { get; }
    TimeSpan Duration { get; }
    int FramesCaptured { get; }
    int FramesEncoded { get; }
    int DroppedFrames { get; }
    int QueueBacklog { get; }
    double MinFrameDeltaMs { get; }
    double MaxFrameDeltaMs { get; }
    double MeanFrameDeltaMs { get; }
    int MonotonicityCorrectionsCount { get; }

    WasapiCaptureService AudioService { get; }
    AudioVideoSyncReport? LastSyncReport { get; }
    AudioDeviceInfo? ActiveAudioDevice { get; }

    void Start(
        string outputFilePath,
        int width,
        int height,
        double targetFps,
        RecordingMode recordingMode,
        SoftwareLookSettings? lookSettings,
        IReadOnlyList<PresetControlValue>? hardwareSettings,
        string captureFormat = "MJPG",
        RecordingCadenceMode cadenceMode = RecordingCadenceMode.CFR_Compatibility,
        bool enableAudio = true,
        AudioDeviceInfo? audioDevice = null);

    void WriteFrame(IntPtr pRgb32, int bufferSize, long sourceTimestampHns, long qpcTicks);

    void Stop();

    RecordingTelemetry GetTelemetry();
    void WriteSidecarV3(string mp4Path, bool isOriginalMode = false);
    IReadOnlyList<FrameTimingAuditRecord> GetTimingAuditRecords();
    void WriteTimingAuditSidecar(string sidecarPath);
}
