using System;
using System.Collections.Generic;
using WebcamLab.Capture.Audio;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Models;

namespace WebcamLab.Capture;

/// <summary>
/// Production FFmpeg subprocess recording backend implementation of IRecorderBackend.
/// Wraps VideoRecorder directly, preserving 100% of the proven FFmpeg recording pipeline,
/// timing policies, filters, and muxing without altering VideoRecorder.cs.
/// </summary>
public class FfmpegRecorderBackend : IRecorderBackend
{
    private readonly VideoRecorder _inner = new();

    public string BackendName => "FFmpeg (Legacy Fallback)";
    public RecordingBackendMode BackendMode => RecordingBackendMode.FFmpeg_Legacy;

    public bool IsRecording => _inner.IsRecording;
    public bool IsFinalizing => _inner.IsFinalizing;
    public string? CurrentFilePath => _inner.CurrentFilePath;
    public TimeSpan Duration => _inner.Duration;
    public int FramesCaptured => _inner.FramesCaptured;
    public int FramesEncoded => _inner.FramesEncoded;
    public int DroppedFrames => _inner.DroppedFrames;
    public int QueueBacklog => _inner.QueueBacklog;
    public double MinFrameDeltaMs => _inner.MinFrameDeltaMs;
    public double MaxFrameDeltaMs => _inner.MaxFrameDeltaMs;
    public double MeanFrameDeltaMs => _inner.MeanFrameDeltaMs;
    public int MonotonicityCorrectionsCount => _inner.MonotonicityCorrectionsCount;

    public WasapiCaptureService AudioService => _inner.AudioService;
    public AudioVideoSyncReport? LastSyncReport => _inner.LastSyncReport;
    public AudioDeviceInfo? ActiveAudioDevice => _inner.ActiveAudioDevice;

    public void Start(
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
        AudioDeviceInfo? audioDevice = null)
    {
        _inner.Start(
            outputFilePath,
            width,
            height,
            targetFps,
            recordingMode,
            lookSettings,
            hardwareSettings,
            captureFormat,
            cadenceMode,
            enableAudio,
            audioDevice);
    }

    public void WriteFrame(IntPtr pRgb32, int bufferSize, long sourceTimestampHns, long qpcTicks)
    {
        _inner.WriteFrame(pRgb32, bufferSize, sourceTimestampHns, qpcTicks);
    }

    public void Stop()
    {
        _inner.Stop();
    }

    public RecordingTelemetry GetTelemetry()
    {
        return _inner.GetTelemetry();
    }

    public void WriteSidecarV3(string mp4Path, bool isOriginalMode = false)
    {
        _inner.WriteSidecarV3(mp4Path, isOriginalMode);
    }

    public IReadOnlyList<FrameTimingAuditRecord> GetTimingAuditRecords()
    {
        return _inner.GetTimingAuditRecords();
    }

    public void WriteTimingAuditSidecar(string sidecarPath)
    {
        _inner.WriteTimingAuditSidecar(sidecarPath);
    }

    public void Dispose()
    {
        _inner.Dispose();
    }
}
