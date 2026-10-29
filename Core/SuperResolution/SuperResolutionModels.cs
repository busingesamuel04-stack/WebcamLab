using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebcamLab.Core.SuperResolution;

/// <summary>
/// Reconstruction algorithm for multi-frame super-resolution experiments.
/// </summary>
public enum SuperResolutionAlgorithm
{
    /// <summary>
    /// Single-frame bicubic interpolation (conventional upscaling baseline).
    /// </summary>
    BicubicSingleFrame = 0,

    /// <summary>
    /// Native-resolution multi-frame denoised frame upscaled via bicubic interpolation (denoised baseline).
    /// </summary>
    DenoisedBicubic = 1,

    /// <summary>
    /// Non-uniform continuous weighted splatting (Shift-and-Add) with normalization.
    /// Accumulates subpixel-shifted samples onto the higher-resolution grid using a Gaussian/Cubic kernel.
    /// </summary>
    ShiftAndAddNormalizedSplatting = 2,

    /// <summary>
    /// Regularized Iterative Back-Projection (IBP) with forward optical PSF modeling
    /// and edge-preserving Tikhonov / Total Variation regularization.
    /// </summary>
    RegularizedIterativeBackProjection = 3
}

/// <summary>
/// Super-resolution output scaling factor.
/// </summary>
public enum SuperResolutionScale
{
    Scale1_5x = 0, // 1920x1080 -> 2880x1620
    Scale2_0x = 1  // 1920x1080 -> 3840x2160
}

/// <summary>
/// Configuration parameters for Phase 26 multi-frame super-resolution research.
/// </summary>
public class SuperResolutionSettings
{
    public SuperResolutionScale Scale { get; set; } = SuperResolutionScale.Scale1_5x;
    public double ScaleFactor => Scale == SuperResolutionScale.Scale1_5x ? 1.5 : 2.0;

    public SuperResolutionAlgorithm Algorithm { get; set; } = SuperResolutionAlgorithm.ShiftAndAddNormalizedSplatting;

    public int BurstSize { get; set; } = 8;
    public double MaxRegistrationShiftPx { get; set; } = 32.0;
    public double MinRegistrationConfidence { get; set; } = 0.65;

    public int IbpIterations { get; set; } = 5;
    public float IbpStepSize { get; set; } = 0.8f;
    public float RegularizationLambda { get; set; } = 0.04f;
    public float PsfSigma { get; set; } = 0.85f;

    public bool SaveIndividualBurstFrames { get; set; } = true;
}

/// <summary>
/// Detailed subpixel shift analysis record for an individual burst frame.
/// </summary>
public class SubPixelShiftAnalysisRecord
{
    [JsonPropertyName("frameIndex")]
    public int FrameIndex { get; set; }

    [JsonPropertyName("sourceTimestampHns")]
    public long SourceTimestampHns { get; set; }

    [JsonPropertyName("deltaFromFirstFrameMs")]
    public double DeltaFromFirstFrameMs { get; set; }

    [JsonPropertyName("shiftX")]
    public double ShiftX { get; set; }

    [JsonPropertyName("shiftY")]
    public double ShiftY { get; set; }

    [JsonPropertyName("fractionalShiftX")]
    public double FractionalShiftX { get; set; }

    [JsonPropertyName("fractionalShiftY")]
    public double FractionalShiftY { get; set; }

    [JsonPropertyName("registrationConfidence")]
    public double RegistrationConfidence { get; set; }

    [JsonPropertyName("shiftUncertaintyPx")]
    public double ShiftUncertaintyPx { get; set; }

    [JsonPropertyName("isReference")]
    public bool IsReference { get; set; }

    [JsonPropertyName("isAccepted")]
    public bool IsAccepted { get; set; } = true;

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; set; }
}

/// <summary>
/// Quantitative spatial quality and artifact metrics for super-resolution evaluation.
/// </summary>
public class SuperResolutionQualityMetrics
{
    [JsonPropertyName("edgeRiseDistance1090Px")]
    public double EdgeRiseDistance1090Px { get; set; }

    [JsonPropertyName("edgeOvershootPercent")]
    public double EdgeOvershootPercent { get; set; }

    [JsonPropertyName("flatRegionNoiseSigma")]
    public double FlatRegionNoiseSigma { get; set; }

    [JsonPropertyName("noiseAmplificationRatio")]
    public double NoiseAmplificationRatio { get; set; }

    [JsonPropertyName("featureSeparationContrast")]
    public double FeatureSeparationContrast { get; set; }

    [JsonPropertyName("registrationResidualRmse")]
    public double RegistrationResidualRmse { get; set; }

    [JsonPropertyName("subpixelDiversityScore")]
    public double SubpixelDiversityScore { get; set; }
}

/// <summary>
/// Companion metadata sidecar (Schema Version 1.0) written for Phase 26 super-resolution captures.
/// </summary>
public class SuperResolutionSidecar
{
    public const string CurrentSchemaVersion = "1.0";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("operationType")]
    public string OperationType { get; set; } = "Multi-Frame Super-Resolution Research";

    [JsonPropertyName("sensorRawClaim")]
    public string SensorRawClaim { get; set; } = "None. Input consists of processed 8-bit YUV/RGB camera bitstreams (YUY2/MJPEG), not Bayer sensor RAW.";

    [JsonPropertyName("cameraDeviceName")]
    public string CameraDeviceName { get; set; } = "Dell Monitor Integrated Webcam";

    [JsonPropertyName("captureFormat")]
    public string CaptureFormat { get; set; } = "YUY2";

    [JsonPropertyName("inputWidth")]
    public int InputWidth { get; set; } = 1920;

    [JsonPropertyName("inputHeight")]
    public int InputHeight { get; set; } = 1080;

    [JsonPropertyName("outputWidth")]
    public int OutputWidth { get; set; }

    [JsonPropertyName("outputHeight")]
    public int OutputHeight { get; set; }

    [JsonPropertyName("scaleFactor")]
    public double ScaleFactor { get; set; } = 1.5;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("researchVerdict")]
    public string ResearchVerdict { get; set; } = string.Empty;

    [JsonPropertyName("totalBurstDurationMs")]
    public double TotalBurstDurationMs { get; set; }

    [JsonPropertyName("processingDurationMs")]
    public double ProcessingDurationMs { get; set; }

    [JsonPropertyName("acceptedFrameCount")]
    public int AcceptedFrameCount { get; set; }

    [JsonPropertyName("rejectedFrameCount")]
    public int RejectedFrameCount { get; set; }

    [JsonPropertyName("frames")]
    public List<SubPixelShiftAnalysisRecord> Frames { get; set; } = [];

    [JsonPropertyName("metrics")]
    public SuperResolutionQualityMetrics Metrics { get; set; } = new();

    [JsonPropertyName("observations")]
    public List<string> Observations { get; set; } = [];
}
