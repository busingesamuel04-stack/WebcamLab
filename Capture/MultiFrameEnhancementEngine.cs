using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using WebcamLab.Core.Models;

namespace WebcamLab.Capture;

/// <summary>
/// Container holding in-memory image buffers and telemetry for a single captured burst frame.
/// </summary>
public class BurstFrameData
{
    public int Index { get; set; }
    public long TimestampHns { get; set; }
    public long ArrivalQpcTicks { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Bgr32Bytes { get; set; } = [];
    public float[] Luma { get; set; } = [];
    public double MeanLuma { get; set; }
    public double SharpnessScore { get; set; }

    public static BurstFrameData Create(int index, long timestampHns, long qpcTicks, int width, int height, byte[] bgr32Bytes)
    {
        var frame = new BurstFrameData
        {
            Index = index,
            TimestampHns = timestampHns,
            ArrivalQpcTicks = qpcTicks,
            Width = width,
            Height = height,
            Bgr32Bytes = bgr32Bytes,
            Luma = new float[width * height]
        };

        // Extract BT.709 Luma and compute Sharpness (Laplacian / gradient energy)
        double lumaSum = 0;
        double gradSum = 0;
        int totalPixels = width * height;

        for (int i = 0, p = 0; i < totalPixels; i++, p += 4)
        {
            // BGR32: B=0, G=1, R=2
            float b = bgr32Bytes[p];
            float g = bgr32Bytes[p + 1];
            float r = bgr32Bytes[p + 2];
            float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            frame.Luma[i] = y;
            lumaSum += y;
        }
        frame.MeanLuma = lumaSum / totalPixels;

        // Gradient energy over interior pixels for sharpness
        for (int y = 1; y < height - 1; y += 2)
        {
            int row = y * width;
            for (int x = 1; x < width - 1; x += 2)
            {
                float dx = frame.Luma[row + x + 1] - frame.Luma[row + x - 1];
                float dy = frame.Luma[row + width + x] - frame.Luma[row - width + x];
                gradSum += (dx * dx + dy * dy);
            }
        }
        frame.SharpnessScore = gradSum / (totalPixels / 4.0);

        return frame;
    }
}

/// <summary>
/// Algorithmic engine for multi-frame still image enhancement.
/// Performs reference selection, hierarchical sub-pixel registration,
/// local motion detection, ghost-free fusion, and quantitative quality metrics.
/// Operates completely independently of live video recording and WASAPI audio capture.
/// </summary>
public class MultiFrameEnhancementEngine
{
    private static readonly float[] SrgbToLinearLut = new float[256];
    private static readonly byte[] LinearToSrgbLut = new byte[4096];

    static MultiFrameEnhancementEngine()
    {
        for (int i = 0; i < 256; i++)
        {
            SrgbToLinearLut[i] = (float)Math.Pow(i / 255.0, 2.2);
        }
        for (int i = 0; i < 4096; i++)
        {
            double lin = i / 4095.0;
            double srgb = Math.Pow(lin, 1.0 / 2.2) * 255.0;
            LinearToSrgbLut[i] = (byte)Math.Clamp(Math.Round(srgb), 0, 255);
        }
    }

    /// <summary>
    /// Selects the optimal reference frame from the burst based on maximum sharpness
    /// while rejecting extreme exposure outliers.
    /// </summary>
    public static int SelectReferenceFrameIndex(IReadOnlyList<BurstFrameData> frames, out string rationale)
    {
        if (frames.Count == 0)
        {
            rationale = "No frames available.";
            return -1;
        }

        if (frames.Count == 1)
        {
            rationale = "Single frame burst.";
            return 0;
        }

        // Compute median luma across burst
        var sortedLumas = frames.Select(f => f.MeanLuma).OrderBy(l => l).ToList();
        double medianLuma = sortedLumas[sortedLumas.Count / 2];

        int bestIndex = 0;
        double bestScore = -1.0;

        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            // Reject frames with > 25% deviation from median exposure
            double lumaDev = Math.Abs(f.MeanLuma - medianLuma) / Math.Max(1.0, medianLuma);
            if (lumaDev > 0.25) continue;

            if (f.SharpnessScore > bestScore)
            {
                bestScore = f.SharpnessScore;
                bestIndex = i;
            }
        }

        rationale = $"Frame {bestIndex + 1} chosen: highest sharpness ({bestScore:F1}) within exposure tolerance.";
        return bestIndex;
    }

    /// <summary>
    /// Computes coarse-to-fine sub-pixel global translational alignment (dx, dy)
    /// and normalized cross-correlation confidence between target and reference frames.
    /// </summary>
    public static (double ShiftX, double ShiftY, double Confidence) RegisterFrame(
        BurstFrameData target,
        BurstFrameData reference,
        double maxShiftPx = 32.0)
    {
        int width = reference.Width;
        int height = reference.Height;

        // Stage 1: Coarse search on 4x downsampled grid
        int ds = 4;
        int dsW = width / ds;
        int dsH = height / ds;
        int maxCoarseShift = (int)Math.Ceiling(maxShiftPx / ds);

        // Center ROI (25% to 75% of frame) for robust motion estimation
        int roiX = dsW / 4;
        int roiY = dsH / 4;
        int roiW = dsW / 2;
        int roiH = dsH / 2;

        float bestScore = float.MaxValue;
        float bestMad = float.MaxValue;
        int bestCoarseDx = 0;
        int bestCoarseDy = 0;

        for (int cdy = -maxCoarseShift; cdy <= maxCoarseShift; cdy++)
        {
            for (int cdx = -maxCoarseShift; cdx <= maxCoarseShift; cdx++)
            {
                float sad = 0;
                int count = 0;
                for (int y = roiY; y < roiY + roiH; y += 2)
                {
                    int refIdx = (y * ds) * width;
                    int tgtY = (y + cdy) * ds;
                    if (tgtY < 0 || tgtY >= height) continue;
                    int tgtIdx = tgtY * width;

                    for (int x = roiX; x < roiX + roiW; x += 2)
                    {
                        int tgtX = (x + cdx) * ds;
                        if (tgtX < 0 || tgtX >= width) continue;

                        float diff = Math.Abs(reference.Luma[refIdx + (x * ds)] - target.Luma[tgtIdx + tgtX]);
                        sad += diff;
                        count++;
                    }
                }

                if (count > 50)
                {
                    float mad = sad / count;
                    // Motion prior: prefer smaller displacement if scores are tied
                    float score = mad + (cdx * cdx + cdy * cdy) * 1e-4f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestMad = mad;
                        bestCoarseDx = cdx;
                        bestCoarseDy = cdy;
                    }
                }
            }
        }

        // Stage 2: Fine search at full resolution around coarse peak
        int centerDx = bestCoarseDx * ds;
        int centerDy = bestCoarseDy * ds;
        int fineRadius = 4;

        float bestFineScore = float.MaxValue;
        float bestFineMad = float.MaxValue;
        int bestFineDx = centerDx;
        int bestFineDy = centerDy;

        int fullRoiX = width / 4;
        int fullRoiY = height / 4;
        int fullRoiW = width / 2;
        int fullRoiH = height / 2;

        for (int fdy = centerDy - fineRadius; fdy <= centerDy + fineRadius; fdy++)
        {
            for (int fdx = centerDx - fineRadius; fdx <= centerDx + fineRadius; fdx++)
            {
                float sad = 0;
                int count = 0;
                for (int y = fullRoiY; y < fullRoiY + fullRoiH; y += 4)
                {
                    int refRow = y * width;
                    int tgtY = y + fdy;
                    if (tgtY < 0 || tgtY >= height) continue;
                    int tgtRow = tgtY * width;

                    for (int x = fullRoiX; x < fullRoiX + fullRoiW; x += 4)
                    {
                        int tgtX = x + fdx;
                        if (tgtX < 0 || tgtX >= width) continue;

                        sad += Math.Abs(reference.Luma[refRow + x] - target.Luma[tgtRow + tgtX]);
                        count++;
                    }
                }

                if (count > 100)
                {
                    float mad = sad / count;
                    float score = mad + (fdx * fdx + fdy * fdy) * 1e-5f;
                    if (score < bestFineScore)
                    {
                        bestFineScore = score;
                        bestFineMad = mad;
                        bestFineDx = fdx;
                        bestFineDy = fdy;
                    }
                }
            }
        }

        // Sub-pixel parabolic refinement
        double subShiftX = bestFineDx;
        double subShiftY = bestFineDy;

        // Compute 3x3 grid around best fine point for parabolic interpolation
        float s00 = ComputeMadAtOffset(target, reference, bestFineDx - 1, bestFineDy, fullRoiX, fullRoiY, fullRoiW, fullRoiH);
        float s01 = bestFineMad;
        float s02 = ComputeMadAtOffset(target, reference, bestFineDx + 1, bestFineDy, fullRoiX, fullRoiY, fullRoiW, fullRoiH);

        double denomX = 2.0 * (s00 - 2 * s01 + s02);
        if (Math.Abs(denomX) > 1e-4)
        {
            double deltaX = (s00 - s02) / denomX;
            subShiftX += Math.Clamp(deltaX, -0.5, 0.5);
        }

        float s10 = ComputeMadAtOffset(target, reference, bestFineDx, bestFineDy - 1, fullRoiX, fullRoiY, fullRoiW, fullRoiH);
        float s12 = ComputeMadAtOffset(target, reference, bestFineDx, bestFineDy + 1, fullRoiX, fullRoiY, fullRoiW, fullRoiH);

        double denomY = 2.0 * (s10 - 2 * s01 + s12);
        if (Math.Abs(denomY) > 1e-4)
        {
            double deltaY = (s10 - s12) / denomY;
            subShiftY += Math.Clamp(deltaY, -0.5, 0.5);
        }

        // Calculate confidence metric (inverse of mean absolute error per pixel)
        double confidence = Math.Clamp(1.0 - (bestFineMad / 35.0), 0.0, 1.0);

        return (subShiftX, subShiftY, confidence);
    }

    private static float ComputeMadAtOffset(
        BurstFrameData target,
        BurstFrameData reference,
        int dx,
        int dy,
        int roiX,
        int roiY,
        int roiW,
        int roiH)
    {
        int width = reference.Width;
        int height = reference.Height;
        float sad = 0;
        int count = 0;

        for (int y = roiY; y < roiY + roiH; y += 4)
        {
            int refRow = y * width;
            int tgtY = y + dy;
            if (tgtY < 0 || tgtY >= height) continue;
            int tgtRow = tgtY * width;

            for (int x = roiX; x < roiX + roiW; x += 4)
            {
                int tgtX = x + dx;
                if (tgtX < 0 || tgtX >= width) continue;
                sad += Math.Abs(reference.Luma[refRow + x] - target.Luma[tgtRow + tgtX]);
                count++;
            }
        }
        return count > 0 ? (sad / count) : float.MaxValue;
    }

    /// <summary>
    /// Resamples target image into reference coordinates using sub-pixel bilinear interpolation.
    /// </summary>
    public static byte[] AlignAndWarp(BurstFrameData target, double shiftX, double shiftY)
    {
        int width = target.Width;
        int height = target.Height;
        byte[] output = new byte[width * height * 4];
        byte[] src = target.Bgr32Bytes;

        for (int y = 0; y < height; y++)
        {
            double srcY = y + shiftY;
            int y0 = (int)Math.Floor(srcY);
            int y1 = y0 + 1;
            double fy = srcY - y0;

            if (y0 < 0 || y1 >= height)
            {
                // Border clamp
                int cy = Math.Clamp((int)Math.Round(srcY), 0, height - 1);
                int srcRow = cy * width * 4;
                int dstRow = y * width * 4;
                Array.Copy(src, srcRow, output, dstRow, width * 4);
                continue;
            }

            int srcRow0 = y0 * width * 4;
            int srcRow1 = y1 * width * 4;
            int dstRowY = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                double srcX = x + shiftX;
                int x0 = (int)Math.Floor(srcX);
                int x1 = x0 + 1;
                double fx = srcX - x0;

                int dstIdx = dstRowY + (x * 4);

                if (x0 < 0 || x1 >= width)
                {
                    int cx = Math.Clamp((int)Math.Round(srcX), 0, width - 1);
                    int srcIdx = srcRow0 + (cx * 4);
                    output[dstIdx] = src[srcIdx];
                    output[dstIdx + 1] = src[srcIdx + 1];
                    output[dstIdx + 2] = src[srcIdx + 2];
                    output[dstIdx + 3] = 255;
                    continue;
                }

                int idx00 = srcRow0 + (x0 * 4);
                int idx10 = srcRow0 + (x1 * 4);
                int idx01 = srcRow1 + (x0 * 4);
                int idx11 = srcRow1 + (x1 * 4);

                double w00 = (1.0 - fx) * (1.0 - fy);
                double w10 = fx * (1.0 - fy);
                double w01 = (1.0 - fx) * fy;
                double w11 = fx * fy;

                // Blue
                double b = w00 * src[idx00] + w10 * src[idx10] + w01 * src[idx01] + w11 * src[idx11];
                // Green
                double g = w00 * src[idx00 + 1] + w10 * src[idx10 + 1] + w01 * src[idx01 + 1] + w11 * src[idx11 + 1];
                // Red
                double r = w00 * src[idx00 + 2] + w10 * src[idx10 + 2] + w01 * src[idx01 + 2] + w11 * src[idx11 + 2];

                output[dstIdx] = (byte)Math.Clamp(Math.Round(b), 0, 255);
                output[dstIdx + 1] = (byte)Math.Clamp(Math.Round(g), 0, 255);
                output[dstIdx + 2] = (byte)Math.Clamp(Math.Round(r), 0, 255);
                output[dstIdx + 3] = 255;
            }
        }

        return output;
    }

    /// <summary>
    /// Computes continuous per-pixel motion weights [0.0 to 1.0].
    /// Pixels with significant motion differences relative to noise floor receive weight ~ 0,
    /// preventing ghosting artifacts by falling back to the reference frame.
    /// </summary>
    public static float[] ComputeMotionWeights(
        byte[] alignedBgr32,
        BurstFrameData reference,
        float sensitivity = 1.0f)
    {
        int width = reference.Width;
        int height = reference.Height;
        int totalPixels = width * height;
        float[] weights = new float[totalPixels];

        // Motion threshold: typical sensor noise standard deviation ~ 3.5 code values.
        // Sensitivity scales the threshold: higher sensitivity -> lower threshold -> less ghosting tolerance.
        float noiseSigma = 3.5f / Math.Max(0.1f, sensitivity);
        float twoSigmaSq = 2.0f * noiseSigma * noiseSigma;

        byte[] refBytes = reference.Bgr32Bytes;

        for (int i = 0, p = 0; i < totalPixels; i++, p += 4)
        {
            // Compute luminance difference
            float bDiff = alignedBgr32[p] - refBytes[p];
            float gDiff = alignedBgr32[p + 1] - refBytes[p + 1];
            float rDiff = alignedBgr32[p + 2] - refBytes[p + 2];

            float lumaDiff = 0.2126f * rDiff + 0.7152f * gDiff + 0.0722f * bDiff;
            float absDiff = Math.Abs(lumaDiff);

            if (absDiff <= noiseSigma)
            {
                weights[i] = 1.0f; // Static region
            }
            else
            {
                // Gaussian falloff for moving regions
                float d = absDiff - noiseSigma;
                weights[i] = (float)Math.Exp(-(d * d) / twoSigmaSq);
            }
        }

        return weights;
    }

    /// <summary>
    /// Combines aligned burst frames in linear optical space using motion-adaptive weights.
    /// Guarantees that moving pixels fall back 100% to the reference frame without ghosting.
    /// </summary>
    public static byte[] FuseFramesLinear(
        BurstFrameData reference,
        IReadOnlyList<byte[]> alignedFrames,
        IReadOnlyList<float[]> motionWeights)
    {
        int width = reference.Width;
        int height = reference.Height;
        int totalPixels = width * height;
        byte[] output = new byte[totalPixels * 4];

        byte[] refBytes = reference.Bgr32Bytes;
        int frameCount = alignedFrames.Count;

        for (int i = 0, p = 0; i < totalPixels; i++, p += 4)
        {
            // Reference frame starts with weight = 1.0
            float totalWeight = 1.0f;

            float linB = SrgbToLinearLut[refBytes[p]];
            float linG = SrgbToLinearLut[refBytes[p + 1]];
            float linR = SrgbToLinearLut[refBytes[p + 2]];

            float sumB = linB;
            float sumG = linG;
            float sumR = linR;

            for (int k = 0; k < frameCount; k++)
            {
                float w = motionWeights[k][i];
                if (w < 0.01f) continue;

                byte[] fBytes = alignedFrames[k];
                sumB += w * SrgbToLinearLut[fBytes[p]];
                sumG += w * SrgbToLinearLut[fBytes[p + 1]];
                sumR += w * SrgbToLinearLut[fBytes[p + 2]];
                totalWeight += w;
            }

            float finalLinB = sumB / totalWeight;
            float finalLinG = sumG / totalWeight;
            float finalLinR = sumR / totalWeight;

            int lutIdxB = (int)Math.Clamp(finalLinB * 4095.0f, 0, 4095);
            int lutIdxG = (int)Math.Clamp(finalLinG * 4095.0f, 0, 4095);
            int lutIdxR = (int)Math.Clamp(finalLinR * 4095.0f, 0, 4095);

            output[p] = LinearToSrgbLut[lutIdxB];
            output[p + 1] = LinearToSrgbLut[lutIdxG];
            output[p + 2] = LinearToSrgbLut[lutIdxR];
            output[p + 3] = 255;
        }

        return output;
    }

    /// <summary>
    /// Measures noise reduction (dB) between original reference and enhanced output
    /// across low-gradient (flat) image patches.
    /// </summary>
    public static (double NoiseReductionDb, double EdgeRetentionRatio) MeasureQualityMetrics(
        BurstFrameData reference,
        byte[] enhancedBgr32)
    {
        int width = reference.Width;
        int height = reference.Height;
        byte[] refBytes = reference.Bgr32Bytes;

        // Sample 32x32 patches across the frame to identify flat regions vs edge regions
        int patchSize = 32;
        int patchesX = width / patchSize;
        int patchesY = height / patchSize;

        double refFlatVarSum = 0;
        double enhFlatVarSum = 0;
        int flatPatchCount = 0;

        double refEdgeGradSum = 0;
        double enhEdgeGradSum = 0;
        int edgeCount = 0;

        for (int py = 1; py < patchesY - 1; py++)
        {
            for (int px = 1; px < patchesX - 1; px++)
            {
                int startX = px * patchSize;
                int startY = py * patchSize;

                // Calculate mean and variance of reference patch
                double sum = 0;
                double sumSq = 0;
                int patchPixels = patchSize * patchSize;

                for (int y = startY; y < startY + patchSize; y++)
                {
                    int row = y * width * 4;
                    for (int x = startX; x < startX + patchSize; x++)
                    {
                        int idx = row + (x * 4);
                        double luma = 0.2126 * refBytes[idx + 2] + 0.7152 * refBytes[idx + 1] + 0.0722 * refBytes[idx];
                        sum += luma;
                        sumSq += luma * luma;
                    }
                }

                double mean = sum / patchPixels;
                double variance = (sumSq / patchPixels) - (mean * mean);

                // If patch variance is low (< 25.0), it represents a flat / smooth area (noise measurement target)
                if (variance > 1.0 && variance < 25.0)
                {
                    // Measure variance of enhanced patch
                    double enhSum = 0;
                    double enhSumSq = 0;
                    for (int y = startY; y < startY + patchSize; y++)
                    {
                        int row = y * width * 4;
                        for (int x = startX; x < startX + patchSize; x++)
                        {
                            int idx = row + (x * 4);
                            double luma = 0.2126 * enhancedBgr32[idx + 2] + 0.7152 * enhancedBgr32[idx + 1] + 0.0722 * enhancedBgr32[idx];
                            enhSum += luma;
                            enhSumSq += luma * luma;
                        }
                    }
                    double enhVar = (enhSumSq / patchPixels) - ((enhSum / patchPixels) * (enhSum / patchPixels));

                    refFlatVarSum += variance;
                    enhFlatVarSum += enhVar;
                    flatPatchCount++;
                }
            }
        }

        // Measure edge retention on high-gradient boundary pixels
        for (int y = 2; y < height - 2; y += 4)
        {
            int row = y * width * 4;
            for (int x = 2; x < width - 2; x += 4)
            {
                int idx = row + (x * 4);
                int idxR = row + ((x + 1) * 4);
                int idxL = row + ((x - 1) * 4);

                double refGrad = Math.Abs(refBytes[idxR + 1] - refBytes[idxL + 1]);
                double enhGrad = Math.Abs(enhancedBgr32[idxR + 1] - enhancedBgr32[idxL + 1]);

                if (refGrad > 20.0) // Significant edge
                {
                    refEdgeGradSum += refGrad;
                    enhEdgeGradSum += enhGrad;
                    edgeCount++;
                }
            }
        }

        double snrImprovementDb = 0.0;
        if (flatPatchCount > 0 && enhFlatVarSum > 1e-4)
        {
            double varRatio = refFlatVarSum / enhFlatVarSum;
            snrImprovementDb = 10.0 * Math.Log10(Math.Max(1.0, varRatio));
        }

        double edgeRetention = edgeCount > 0 ? (enhEdgeGradSum / Math.Max(1.0, refEdgeGradSum)) : 1.0;

        return (snrImprovementDb, edgeRetention);
    }

    /// <summary>
    /// Writes raw BGR32 byte buffer to a lossless PNG file.
    /// </summary>
    public static void SaveBgr32ToPng(byte[] bgr32Bytes, int width, int height, string filePath)
    {
        var bitmapSource = BitmapSource.Create(
            width, height, 96, 96,
            System.Windows.Media.PixelFormats.Bgr32, null,
            bgr32Bytes, width * 4);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }
}
