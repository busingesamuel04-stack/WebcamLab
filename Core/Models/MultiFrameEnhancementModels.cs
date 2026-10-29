using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebcamLab.Core.Models;

/// <summary>
/// Temporal image fusion algorithm for multi-frame still enhancement.
/// </summary>
public enum MultiFrameFusionAlgorithm
{
    /// <summary>
    /// Motion-adaptive weighted mean in linear RGB space.
    /// Provides theoretical 1/sqrt(N) noise reduction in static regions while falling back
    /// to the reference frame in moving regions to prevent ghosting.
    /// </summary>
    MotionAdaptiveWeightedMean = 0,

    /// <summary>
    /// Temporal median estimator across aligned frames.
    /// Highly robust against impulsive noise and transient occlusions.
    /// </summary>
    TemporalMedian = 1,

    /// <summary>
    /// Trimmed mean excluding highest and lowest outliers before averaging.
    /// </summary>
    TrimmedMean = 2
}

/// <summary>
/// Configuration parameters for multi-frame still burst capture and enhancement.
/// </summary>
public class MultiFrameBurstSettings
{
    /// <summary>
    /// Number of genuine unique frames to collect in the burst (e.g. 4, 8, 12).
    /// </summary>
    public int BurstSize { get; set; } = 8;

    /// <summary>
    /// Temporal combination algorithm.
    /// </summary>
    public MultiFrameFusionAlgorithm Algorithm { get; set; } = MultiFrameFusionAlgorithm.MotionAdaptiveWeightedMean;

    /// <summary>
    /// Motion sensitivity threshold multiplier (default 1.0).
    /// Lower values are more aggressive at detecting motion (preventing ghosting at the cost of less denoising in questionable areas).
    /// </summary>
    public float MotionSensitivity { get; set; } = 1.0f;

    /// <summary>
    /// Maximum allowable global registration shift in pixels before a frame is rejected (default 32.0 px).
    /// </summary>
    public double MaxRegistrationShiftPx { get; set; } = 32.0;

    /// <summary>
    /// Minimum normalized cross-correlation confidence required to accept a frame's alignment (default 0.60).
    /// </summary>
    public double MinRegistrationConfidence { get; set; } = 0.60;

    /// <summary>
    /// Whether to save all individual burst frames for forensic inspection.
    /// </summary>
    public bool SaveIndividualBurstFrames { get; set; } = false;
}

/// <summary>
/// Detailed telemetry and alignment audit for a single frame within a multi-frame burst.
/// </summary>
public class BurstFrameAuditRecord
{
    [JsonPropertyName("frameIndex")]
    public int FrameIndex { get; set; }

    [JsonPropertyName("sourceTimestampHns")]
    public long SourceTimestampHns { get; set; }

    [JsonPropertyName("arrivalQpcTicks")]
    public long ArrivalQpcTicks { get; set; }

    [JsonPropertyName("deltaFromFirstFrameMs")]
    public double DeltaFromFirstFrameMs { get; set; }

    [JsonPropertyName("meanLuma")]
    public double MeanLuma { get; set; }

    [JsonPropertyName("sharpnessScore")]
    public double SharpnessScore { get; set; }

    [JsonPropertyName("isReferenceFrame")]
    public bool IsReferenceFrame { get; set; }

    [JsonPropertyName("isAccepted")]
    public bool IsAccepted { get; set; } = true;

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; set; }

    [JsonPropertyName("alignmentShiftX")]
    public double AlignmentShiftX { get; set; }

    [JsonPropertyName("alignmentShiftY")]
    public double AlignmentShiftY { get; set; }

    [JsonPropertyName("registrationConfidence")]
    public double RegistrationConfidence { get; set; } = 1.0;
}

/// <summary>
/// Versioned companion metadata sidecar (Schema Version 1.0) written alongside
/// multi-frame enhanced still captures.
/// </summary>
public class MultiFrameEnhancementSidecar
{
    public const string CurrentSchemaVersion = "1.0";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("operationType")]
    public string OperationType { get; set; } = "Multi-Frame Still Image Denoising & Fusion";

    [JsonPropertyName("sensorRawClaim")]
    public string SensorRawClaim { get; set; } = "None. Input consists of processed 8-bit YUV/RGB camera bitstreams (YUY2/MJPEG), not Bayer sensor RAW.";

    [JsonPropertyName("cameraDeviceName")]
    public string CameraDeviceName { get; set; } = "Dell Monitor Integrated Webcam";

    [JsonPropertyName("captureFormat")]
    public string CaptureFormat { get; set; } = "YUY2";

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("burstSizeRequested")]
    public int BurstSizeRequested { get; set; }

    [JsonPropertyName("uniqueFramesCollected")]
    public int UniqueFramesCollected { get; set; }

    [JsonPropertyName("framesAcceptedForFusion")]
    public int FramesAcceptedForFusion { get; set; }

    [JsonPropertyName("framesRejectedCount")]
    public int FramesRejectedCount { get; set; }

    [JsonPropertyName("referenceFrameIndex")]
    public int ReferenceFrameIndex { get; set; }

    [JsonPropertyName("referenceSelectionCriterion")]
    public string ReferenceSelectionCriterion { get; set; } = "Max Laplacian gradient energy with median exposure stability";

    [JsonPropertyName("fusionAlgorithm")]
    public string FusionAlgorithm { get; set; } = "Motion-Adaptive Weighted Mean (Linear RGB)";

    [JsonPropertyName("totalBurstCaptureDurationMs")]
    public double TotalBurstCaptureDurationMs { get; set; }

    [JsonPropertyName("processingDurationMs")]
    public double ProcessingDurationMs { get; set; }

    [JsonPropertyName("motionMaskPixelPercentage")]
    public double MotionMaskPixelPercentage { get; set; }

    [JsonPropertyName("measuredNoiseReductionDb")]
    public double MeasuredNoiseReductionDb { get; set; }

    [JsonPropertyName("measuredEdgeRetentionRatio")]
    public double MeasuredEdgeRetentionRatio { get; set; }

    [JsonPropertyName("originalImagePath")]
    public string OriginalImagePath { get; set; } = string.Empty;

    [JsonPropertyName("enhancedImagePath")]
    public string EnhancedImagePath { get; set; } = string.Empty;

    [JsonPropertyName("sidecarPath")]
    public string SidecarPath { get; set; } = string.Empty;

    [JsonPropertyName("frames")]
    public List<BurstFrameAuditRecord> Frames { get; set; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];
}
