using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebcamLab.Core.Models;

/// <summary>
/// Defines the temporal pacing and frame cadence policy for video recording.
/// </summary>
public enum RecordingCadenceMode
{
    /// <summary>
    /// Constant Frame Rate compatibility mode (nominal 30 FPS).
    /// Duplicates frames when capture delivery falls below nominal rate to maintain fixed duration timeline.
    /// </summary>
    CFR_Compatibility = 0,

    /// <summary>
    /// Capture-Faithful Variable Frame Rate mode (source-timestamp-preserving).
    /// Preserves true frame delivery intervals and timestamps; zero duplicate padding frames.
    /// </summary>
    Capture_Faithful_VFR = 1
}

/// <summary>
/// Explicit recording mode defining whether encoded video captures the pristine camera buffer
/// or the non-destructive software look pipeline presentation buffer.
/// </summary>
public enum RecordingMode
{
    /// <summary>
    /// Encodes pristine decoded camera frames directly from Media Foundation SourceReader (_bgr32Buffer).
    /// Software look transforms, 3D LUTs, false color, and zebras are 100% bypassed.
    /// </summary>
    OriginalCamera = 0,

    /// <summary>
    /// Encodes frames processed by the Software Look Pipeline (_processedBuffer), baking active
    /// tone adjustments, digital gain, and 3D LUTs into the MP4 bitstream.
    /// False color, zebras, and HUD overlays remain strictly excluded.
    /// </summary>
    GradedVideo = 1
}

/// <summary>
/// Detailed per-frame audit record comparing source hardware timestamp to normalized presentation time.
/// </summary>
public class FrameTimingAuditRecord
{
    public int FrameIndex { get; set; }
    public long OriginalSourceTimestampHns { get; set; }
    public double RawSourceTimeSec { get; set; }
    public double NormalizedTimeSec { get; set; }
    public bool MonotonicityCorrected { get; set; }
    public double CorrectionDeltaMs { get; set; }
    public double InterFrameDeltaMs { get; set; }
    public int RepeatCount { get; set; } = 1;
}

/// <summary>
/// Versioned JSON metadata sidecar (Schema Version 3.0) written alongside finalized media files.
/// Captures camera provenance, encoding statistics, cadence policy, audio telemetry,
/// A/V synchronization, software look parameters, and session diagnostics.
/// Fully backward-compatible with legacy GradedVideoMetadata consumers.
/// </summary>
public class RecordingSidecarV3
{
    public const string CurrentSchemaVersion = "3.0";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("recordingMode")]
    public string RecordingMode { get; set; } = "Graded"; // "Graded" or "Original"

    [JsonPropertyName("cadenceMode")]
    public string CadenceMode { get; set; } = "CFR_Compatibility";

    [JsonPropertyName("videoFileName")]
    public string VideoFileName { get; set; } = string.Empty;

    [JsonPropertyName("videoFilePath")]
    public string VideoFilePath { get; set; } = string.Empty;

    [JsonPropertyName("recordedAtUtc")]
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("completedAtUtc")]
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    // Camera & Format Identity
    [JsonPropertyName("cameraDeviceName")]
    public string CameraDeviceName { get; set; } = "Unknown Camera";

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("captureFormat")]
    public string CaptureFormat { get; set; } = "MJPG";

    [JsonPropertyName("targetFps")]
    public double TargetFps { get; set; }

    [JsonPropertyName("achievedFps")]
    public double AchievedFps { get; set; }

    // Frame Statistics
    [JsonPropertyName("totalFramesCaptured")]
    public int TotalFramesCaptured { get; set; }

    [JsonPropertyName("totalFramesEncoded")]
    public int TotalFramesEncoded { get; set; }

    [JsonPropertyName("droppedFrames")]
    public int DroppedFrames { get; set; }

    [JsonPropertyName("paddedDuplicateFrames")]
    public int PaddedDuplicateFrames { get; set; }

    // Timing Integrity
    [JsonPropertyName("sourceTimestampsPreserved")]
    public bool SourceTimestampsPreserved { get; set; }

    [JsonPropertyName("timingPolicy")]
    public string TimingPolicy { get; set; } = "Constant Frame Rate (CFR) with presentation timestamp pacing";

    [JsonPropertyName("minFrameDeltaMs")]
    public double MinFrameDeltaMs { get; set; }

    [JsonPropertyName("maxFrameDeltaMs")]
    public double MaxFrameDeltaMs { get; set; }

    [JsonPropertyName("meanFrameDeltaMs")]
    public double MeanFrameDeltaMs { get; set; }

    [JsonPropertyName("monotonicityCorrectionsCount")]
    public int MonotonicityCorrectionsCount { get; set; }

    [JsonPropertyName("initialSourceTimestampHns")]
    public long InitialSourceTimestampHns { get; set; }

    [JsonPropertyName("finalSourceTimestampHns")]
    public long FinalSourceTimestampHns { get; set; }

    [JsonPropertyName("sourceDurationSeconds")]
    public double SourceDurationSeconds { get; set; }

    [JsonPropertyName("encoderBackend")]
    public string EncoderBackend { get; set; } = "FFmpeg H.264 (libx264, yuv420p, crf 18, ultrafast)";

    [JsonPropertyName("frameTimingAuditTrail")]
    public List<FrameTimingAuditRecord> FrameTimingAuditTrail { get; set; } = [];

    // Software Look & LUT Telemetry
    [JsonPropertyName("softwareLook")]
    public SoftwareLookSettings SoftwareLook { get; set; } = new();

    [JsonPropertyName("lutStatus")]
    public string LutStatus { get; set; } = "None"; // "Ready", "Missing", "Incompatible", "None"

    [JsonPropertyName("lutStatusDetails")]
    public string? LutStatusDetails { get; set; }

    // Hardware State Telemetry
    [JsonPropertyName("realtekUnit7Control2")]
    public string? RealtekUnit7Control2 { get; set; }

    [JsonPropertyName("hardwareControls")]
    public List<PresetControlValue> HardwareControls { get; set; } = [];

    // Audio Capture & Telemetry (WASAPI)
    [JsonPropertyName("audioEnabled")]
    public bool AudioEnabled { get; set; }

    [JsonPropertyName("audioDeviceName")]
    public string? AudioDeviceName { get; set; }

    [JsonPropertyName("audioDeviceId")]
    public string? AudioDeviceId { get; set; }

    [JsonPropertyName("audioSampleRate")]
    public int? AudioSampleRate { get; set; }

    [JsonPropertyName("audioChannels")]
    public int? AudioChannels { get; set; }

    [JsonPropertyName("audioCodec")]
    public string? AudioCodec { get; set; }

    [JsonPropertyName("audioDurationSeconds")]
    public double? AudioDurationSeconds { get; set; }

    [JsonPropertyName("audioDiscontinuitiesCount")]
    public int? AudioDiscontinuitiesCount { get; set; }

    [JsonPropertyName("silencePacketsCount")]
    public int? SilencePacketsCount { get; set; }

    // A/V Clock Synchronization
    [JsonPropertyName("initialAudioVideoOffsetMs")]
    public double? InitialAudioVideoOffsetMs { get; set; }

    [JsonPropertyName("middleAudioVideoOffsetMs")]
    public double? MiddleAudioVideoOffsetMs { get; set; }

    [JsonPropertyName("finalAudioVideoOffsetMs")]
    public double? FinalAudioVideoOffsetMs { get; set; }

    [JsonPropertyName("audioVideoDriftMs")]
    public double? AudioVideoDriftMs { get; set; }

    [JsonPropertyName("driftRateMsPerMin")]
    public double? DriftRateMsPerMin { get; set; }

    [JsonPropertyName("clockPpm")]
    public double? ClockPpm { get; set; }

    // Workstation Session Interruption & Diagnostic Warnings
    [JsonPropertyName("sessionInterrupted")]
    public bool SessionInterrupted { get; set; }

    [JsonPropertyName("cumulativeSessionLockDurationSec")]
    public double CumulativeSessionLockDurationSec { get; set; }

    [JsonPropertyName("sessionEvents")]
    public List<WebcamLab.Core.Session.SessionInterruptionEvent> SessionEvents { get; set; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Companion JSON metadata sidecar written alongside Graded Video MP4 recordings.
/// Inherits from RecordingSidecarV3 to guarantee full backward compatibility.
/// </summary>
public class GradedVideoMetadata : RecordingSidecarV3
{
}

/// <summary>
/// Live telemetry for video recording.
/// </summary>
public class RecordingTelemetry
{
    public bool IsRecording { get; set; }
    public bool IsFinalizing { get; set; }
    public RecordingMode Mode { get; set; } = RecordingMode.OriginalCamera;
    public RecordingCadenceMode CadenceMode { get; set; } = RecordingCadenceMode.CFR_Compatibility;
    public string? CurrentFilePath { get; set; }
    public TimeSpan Duration { get; set; }
    public int FramesCaptured { get; set; }
    public int FramesEncoded { get; set; }
    public int PaddedDuplicateFrames { get; set; }
    public int QueueBacklog { get; set; }
    public int DroppedFrames { get; set; }
    public double EncodingFps { get; set; }
    public double OutputBitrateKbps { get; set; }

    // Live audio telemetry
    public bool IsAudioActive { get; set; }
    public float AudioPeakLevel { get; set; }
    public float AudioPeakDb { get; set; }
    public float AudioRmsDb { get; set; }
    public bool IsAudioClipping { get; set; }
    public int AudioDiscontinuitiesCount { get; set; }
    public double AudioVideoOffsetMs { get; set; }
}
