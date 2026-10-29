using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WebcamLab.Core.Models;

namespace WebcamLab.Capture;

/// <summary>
/// Service managing multi-frame still image enhancement processing and sidecar persistence.
/// </summary>
public static class MultiFrameCaptureService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Processes a collected burst of camera frames, performing reference selection,
    /// sub-pixel motion registration, ghost-free linear fusion, metrics calculation,
    /// and saving both original and enhanced images with a versioned sidecar.
    /// </summary>
    public static MultiFrameEnhancementSidecar ProcessBurst(
        IReadOnlyList<BurstFrameData> frames,
        string outputDirectory,
        MultiFrameBurstSettings settings,
        string cameraDeviceName,
        string captureFormat)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("Cannot process burst: No frames collected.", nameof(frames));
        }

        var sw = Stopwatch.StartNew();

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        int width = frames[0].Width;
        int height = frames[0].Height;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

        // 1. Compute burst collection duration
        double totalBurstDurationMs = 0;
        if (frames.Count > 1)
        {
            long startTick = frames[0].ArrivalQpcTicks;
            long endTick = frames[^1].ArrivalQpcTicks;
            totalBurstDurationMs = (endTick - startTick) * 1000.0 / Stopwatch.Frequency;
        }

        // 2. Select reference frame (optimal sharpness + stable exposure)
        int refIndex = MultiFrameEnhancementEngine.SelectReferenceFrameIndex(frames, out string refRationale);
        var refFrame = frames[refIndex];

        var auditRecords = new List<BurstFrameAuditRecord>();
        var alignedFrames = new List<byte[]>();
        var motionWeightsList = new List<float[]>();
        var warnings = new List<string>();

        int acceptedCount = 0;
        int rejectedCount = 0;
        double motionPixelAccum = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            double deltaMs = (f.ArrivalQpcTicks - frames[0].ArrivalQpcTicks) * 1000.0 / Stopwatch.Frequency;

            var audit = new BurstFrameAuditRecord
            {
                FrameIndex = f.Index,
                SourceTimestampHns = f.TimestampHns,
                ArrivalQpcTicks = f.ArrivalQpcTicks,
                DeltaFromFirstFrameMs = deltaMs,
                MeanLuma = f.MeanLuma,
                SharpnessScore = f.SharpnessScore,
                IsReferenceFrame = (i == refIndex)
            };

            if (i == refIndex)
            {
                audit.IsAccepted = true;
                audit.AlignmentShiftX = 0;
                audit.AlignmentShiftY = 0;
                audit.RegistrationConfidence = 1.0;
                auditRecords.Add(audit);
                acceptedCount++;
                continue;
            }

            // Estimate global motion shift
            var (shiftX, shiftY, confidence) = MultiFrameEnhancementEngine.RegisterFrame(f, refFrame, settings.MaxRegistrationShiftPx);
            audit.AlignmentShiftX = shiftX;
            audit.AlignmentShiftY = shiftY;
            audit.RegistrationConfidence = confidence;

            // Evaluate rejection criteria
            if (Math.Abs(shiftX) > settings.MaxRegistrationShiftPx || Math.Abs(shiftY) > settings.MaxRegistrationShiftPx)
            {
                audit.IsAccepted = false;
                audit.RejectionReason = $"Excessive camera displacement ({shiftX:F1}, {shiftY:F1} px) exceeding threshold ({settings.MaxRegistrationShiftPx:F1} px).";
                rejectedCount++;
                warnings.Add($"Frame {f.Index} rejected: Large motion displacement ({shiftX:F1}, {shiftY:F1} px).");
            }
            else if (confidence < settings.MinRegistrationConfidence)
            {
                audit.IsAccepted = false;
                audit.RejectionReason = $"Low alignment correlation ({confidence:F2}) below threshold ({settings.MinRegistrationConfidence:F2}).";
                rejectedCount++;
                warnings.Add($"Frame {f.Index} rejected: Low alignment correlation ({confidence:F2}).");
            }
            else
            {
                // Align and compute motion weights
                byte[] aligned = MultiFrameEnhancementEngine.AlignAndWarp(f, shiftX, shiftY);
                float[] weights = MultiFrameEnhancementEngine.ComputeMotionWeights(aligned, refFrame, settings.MotionSensitivity);

                // Calculate percentage of moving pixels in this frame
                int movingPixels = 0;
                for (int p = 0; p < weights.Length; p++)
                {
                    if (weights[p] < 0.5f) movingPixels++;
                }
                motionPixelAccum += (double)movingPixels / weights.Length;

                alignedFrames.Add(aligned);
                motionWeightsList.Add(weights);
                audit.IsAccepted = true;
                acceptedCount++;
            }

            auditRecords.Add(audit);
        }

        // 3. Temporal combination
        byte[] enhancedBgr32;
        if (alignedFrames.Count == 0)
        {
            // Fallback to reference frame directly if all others were rejected
            enhancedBgr32 = (byte[])refFrame.Bgr32Bytes.Clone();
            warnings.Add("All secondary burst frames rejected due to excessive motion or low correlation. Output fell back to reference frame.");
        }
        else
        {
            enhancedBgr32 = MultiFrameEnhancementEngine.FuseFramesLinear(refFrame, alignedFrames, motionWeightsList);
        }

        // 4. Calculate quality metrics
        var (noiseReductionDb, edgeRetentionRatio) = MultiFrameEnhancementEngine.MeasureQualityMetrics(refFrame, enhancedBgr32);

        // 5. File persistence
        string origFilename = $"WebcamLab_Still_Original_{width}x{height}_{timestamp}.png";
        string origPath = Path.Combine(outputDirectory, origFilename);
        MultiFrameEnhancementEngine.SaveBgr32ToPng(refFrame.Bgr32Bytes, width, height, origPath);

        string enhFilename = $"WebcamLab_Still_Enhanced_{width}x{height}_{timestamp}.png";
        string enhPath = Path.Combine(outputDirectory, enhFilename);
        MultiFrameEnhancementEngine.SaveBgr32ToPng(enhancedBgr32, width, height, enhPath);

        if (settings.SaveIndividualBurstFrames)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                string bFilename = $"WebcamLab_Burst_Frame{i + 1}_{width}x{height}_{timestamp}.png";
                MultiFrameEnhancementEngine.SaveBgr32ToPng(frames[i].Bgr32Bytes, width, height, Path.Combine(outputDirectory, bFilename));
            }
        }

        sw.Stop();

        // 6. Build companion sidecar
        double motionPct = alignedFrames.Count > 0 ? (motionPixelAccum / alignedFrames.Count) * 100.0 : 0.0;
        string sidecarFilename = $"WebcamLab_Still_Enhanced_{width}x{height}_{timestamp}.json";
        string sidecarPath = Path.Combine(outputDirectory, sidecarFilename);

        var sidecar = new MultiFrameEnhancementSidecar
        {
            SchemaVersion = "1.0",
            CameraDeviceName = cameraDeviceName,
            CaptureFormat = captureFormat,
            Width = width,
            Height = height,
            BurstSizeRequested = settings.BurstSize,
            UniqueFramesCollected = frames.Count,
            FramesAcceptedForFusion = acceptedCount,
            FramesRejectedCount = rejectedCount,
            ReferenceFrameIndex = refIndex,
            ReferenceSelectionCriterion = refRationale,
            FusionAlgorithm = settings.Algorithm.ToString(),
            TotalBurstCaptureDurationMs = totalBurstDurationMs,
            ProcessingDurationMs = sw.Elapsed.TotalMilliseconds,
            MotionMaskPixelPercentage = motionPct,
            MeasuredNoiseReductionDb = noiseReductionDb,
            MeasuredEdgeRetentionRatio = edgeRetentionRatio,
            OriginalImagePath = origPath,
            EnhancedImagePath = enhPath,
            SidecarPath = sidecarPath,
            Frames = auditRecords,
            Warnings = warnings
        };

        string sidecarJson = JsonSerializer.Serialize(sidecar, JsonOptions);
        File.WriteAllText(sidecarPath, sidecarJson);

        return sidecar;
    }
}
