using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using WebcamLab.Capture;

namespace WebcamLab.Core.SuperResolution;

/// <summary>
/// Core algorithmic engine for multi-frame super-resolution research.
/// Implements subpixel phase correlation, non-uniform sample splatting,
/// regularized iterative back-projection, and quantitative resolution / MTF metrics.
/// </summary>
public class SuperResolutionEngine
{
    private static readonly float[] SrgbToLinearLut = new float[256];
    private static readonly byte[] LinearToSrgbLut = new byte[4096];

    static SuperResolutionEngine()
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SrgbToLinear(byte val) => SrgbToLinearLut[val];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte LinearToSrgb(float val)
    {
        if (float.IsNaN(val) || val <= 0.0f) return 0;
        if (val >= 1.0f) return 255;
        int idx = (int)(val * 4095.0f);
        return LinearToSrgbLut[Math.Clamp(idx, 0, 4095)];
    }

    /// <summary>
    /// Analyzes subpixel shifts across a burst relative to the reference frame.
    /// Returns per-frame shift telemetry, fractional coordinates, and sampling diversity score.
    /// </summary>
    public static List<SubPixelShiftAnalysisRecord> AnalyzeBurstShifts(
        IReadOnlyList<BurstFrameData> frames,
        int refIndex,
        double maxShiftPx = 32.0,
        double minConfidence = 0.65)
    {
        var records = new List<SubPixelShiftAnalysisRecord>();
        var refFrame = frames[refIndex];

        long firstTimestamp = frames[0].TimestampHns;

        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            double deltaMs = (f.TimestampHns - firstTimestamp) / 10000.0;

            if (i == refIndex)
            {
                records.Add(new SubPixelShiftAnalysisRecord
                {
                    FrameIndex = i,
                    SourceTimestampHns = f.TimestampHns,
                    DeltaFromFirstFrameMs = deltaMs,
                    ShiftX = 0.0,
                    ShiftY = 0.0,
                    FractionalShiftX = 0.0,
                    FractionalShiftY = 0.0,
                    RegistrationConfidence = 1.0,
                    ShiftUncertaintyPx = 0.01,
                    IsReference = true,
                    IsAccepted = true
                });
                continue;
            }

            var (sx, sy, conf) = MultiFrameEnhancementEngine.RegisterFrame(f, refFrame, maxShiftPx);

            // Calculate fractional subpixel shifts in [0, 1)
            double fracX = sx - Math.Floor(sx);
            double fracY = sy - Math.Floor(sy);

            // Estimate uncertainty: higher when confidence is low or displacement is large
            double uncertainty = Math.Max(0.02, (1.0 - conf) * 0.5 + Math.Sqrt(sx * sx + sy * sy) * 0.01);

            bool accepted = true;
            string? reason = null;

            if (conf < minConfidence)
            {
                accepted = false;
                reason = $"Low registration confidence ({conf:F2} < {minConfidence:F2})";
            }
            else if (Math.Abs(sx) > maxShiftPx || Math.Abs(sy) > maxShiftPx)
            {
                accepted = false;
                reason = $"Excessive motion shift (dx={sx:F1}, dy={sy:F1} > {maxShiftPx:F1} px)";
            }

            records.Add(new SubPixelShiftAnalysisRecord
            {
                FrameIndex = i,
                SourceTimestampHns = f.TimestampHns,
                DeltaFromFirstFrameMs = deltaMs,
                ShiftX = sx,
                ShiftY = sy,
                FractionalShiftX = fracX,
                FractionalShiftY = fracY,
                RegistrationConfidence = conf,
                ShiftUncertaintyPx = uncertainty,
                IsReference = false,
                IsAccepted = accepted,
                RejectionReason = reason
            });
        }

        return records;
    }

    /// <summary>
    /// Computes the 2D subpixel sampling diversity score in [0.0, 1.0].
    /// Measures how uniformly the fractional sampling phases cover the unit cell [0, 1) x [0, 1).
    /// A score near 0 indicates all frames have redundant sampling phases (no super-resolution possible).
    /// </summary>
    public static double ComputeSubpixelDiversityScore(IEnumerable<SubPixelShiftAnalysisRecord> acceptedRecords)
    {
        var valid = acceptedRecords.Where(r => r.IsAccepted).ToList();
        if (valid.Count <= 1) return 0.0;

        // Partition the unit cell [0, 1) x [0, 1) into 4 quadrants
        int[] quadCounts = new int[4];
        foreach (var r in valid)
        {
            int qx = r.FractionalShiftX >= 0.5 ? 1 : 0;
            int qy = r.FractionalShiftY >= 0.5 ? 1 : 0;
            int quad = qy * 2 + qx;
            quadCounts[quad]++;
        }

        // Count populated quadrants
        int populated = quadCounts.Count(c => c > 0);
        double quadCoverage = populated / 4.0;

        // Compute mean pairwise distance in modulo-1 2D space
        double totalDist = 0;
        int pairs = 0;
        for (int i = 0; i < valid.Count; i++)
        {
            for (int j = i + 1; j < valid.Count; j++)
            {
                double dx = Math.Abs(valid[i].FractionalShiftX - valid[j].FractionalShiftX);
                if (dx > 0.5) dx = 1.0 - dx;
                double dy = Math.Abs(valid[i].FractionalShiftY - valid[j].FractionalShiftY);
                if (dy > 0.5) dy = 1.0 - dy;
                totalDist += Math.Sqrt(dx * dx + dy * dy);
                pairs++;
            }
        }

        double meanPairwiseDist = pairs > 0 ? (totalDist / pairs) : 0;
        // Maximum theoretical mean distance in unit torus is ~0.38
        double normalizedDist = Math.Clamp(meanPairwiseDist / 0.35, 0.0, 1.0);

        return Math.Round(0.5 * quadCoverage + 0.5 * normalizedDist, 3);
    }

    /// <summary>
    /// Baseline 0: Conventional single-frame upscaling using a Keys bicubic filter (Catmull-Rom spline, a = -0.5).
    /// </summary>
    public static byte[] UpscaleBicubic(BurstFrameData frame, double scaleFactor, out int outWidth, out int outHeight)
    {
        int inW = frame.Width;
        int inH = frame.Height;
        outWidth = (int)Math.Round(inW * scaleFactor);
        outHeight = (int)Math.Round(inH * scaleFactor);

        byte[] src = frame.Bgr32Bytes;
        byte[] dst = new byte[outWidth * outHeight * 4];

        double invScale = 1.0 / scaleFactor;

        for (int y = 0; y < outHeight; y++)
        {
            double srcY = (y + 0.5) * invScale - 0.5;
            int y0 = (int)Math.Floor(srcY);
            double fy = srcY - y0;

            int dstRow = y * outWidth * 4;

            for (int x = 0; x < outWidth; x++)
            {
                double srcX = (x + 0.5) * invScale - 0.5;
                int x0 = (int)Math.Floor(srcX);
                double fx = srcX - x0;

                float b = 0, g = 0, r = 0;

                for (int m = -1; m <= 2; m++)
                {
                    int sy = Math.Clamp(y0 + m, 0, inH - 1);
                    float wy = BicubicWeight((float)(fy - m));
                    int srcRow = sy * inW * 4;

                    for (int n = -1; n <= 2; n++)
                    {
                        int sx = Math.Clamp(x0 + n, 0, inW - 1);
                        float wx = BicubicWeight((float)(fx - n));
                        float w = wx * wy;

                        int idx = srcRow + sx * 4;
                        b += src[idx] * w;
                        g += src[idx + 1] * w;
                        r += src[idx + 2] * w;
                    }
                }

                int dstIdx = dstRow + x * 4;
                dst[dstIdx] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
                dst[dstIdx + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
                dst[dstIdx + 2] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
                dst[dstIdx + 3] = 255;
            }
        }

        return dst;
    }

    /// <summary>
    /// Baseline 0b: Conventional single-frame upscaling using a 3-lobe Lanczos filter (Lanczos3).
    /// </summary>
    public static byte[] UpscaleLanczos3(BurstFrameData frame, double scaleFactor, out int outWidth, out int outHeight)
    {
        int inW = frame.Width;
        int inH = frame.Height;
        outWidth = (int)Math.Round(inW * scaleFactor);
        outHeight = (int)Math.Round(inH * scaleFactor);

        byte[] src = frame.Bgr32Bytes;
        byte[] dst = new byte[outWidth * outHeight * 4];
        double invScale = 1.0 / scaleFactor;

        for (int y = 0; y < outHeight; y++)
        {
            double srcY = (y + 0.5) * invScale - 0.5;
            int y0 = (int)Math.Floor(srcY);
            int dstRow = y * outWidth * 4;

            for (int x = 0; x < outWidth; x++)
            {
                double srcX = (x + 0.5) * invScale - 0.5;
                int x0 = (int)Math.Floor(srcX);

                float b = 0, g = 0, r = 0, wSum = 0;

                for (int m = -2; m <= 3; m++)
                {
                    int sy = Math.Clamp(y0 + m, 0, inH - 1);
                    float wy = Lanczos3Weight((float)(srcY - (y0 + m)));
                    if (Math.Abs(wy) < 1e-5f) continue;
                    int srcRow = sy * inW * 4;

                    for (int n = -2; n <= 3; n++)
                    {
                        int sx = Math.Clamp(x0 + n, 0, inW - 1);
                        float wx = Lanczos3Weight((float)(srcX - (x0 + n)));
                        float w = wx * wy;
                        if (Math.Abs(w) < 1e-6f) continue;

                        int idx = srcRow + sx * 4;
                        b += src[idx] * w;
                        g += src[idx + 1] * w;
                        r += src[idx + 2] * w;
                        wSum += w;
                    }
                }

                if (wSum > 1e-4f)
                {
                    b /= wSum;
                    g /= wSum;
                    r /= wSum;
                }

                int dstIdx = dstRow + x * 4;
                dst[dstIdx] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
                dst[dstIdx + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
                dst[dstIdx + 2] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
                dst[dstIdx + 3] = 255;
            }
        }

        return dst;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lanczos3Weight(float x)
    {
        x = Math.Abs(x);
        if (x < 1e-5f) return 1.0f;
        if (x >= 3.0f) return 0.0f;
        double piX = Math.PI * x;
        return (float)((Math.Sin(piX) / piX) * (Math.Sin(piX / 3.0) / (piX / 3.0)));
    }

    /// <summary>
    /// Analyzes independent X-axis, Y-axis, and 2D subpixel phase coverage across accepted frames.
    /// Eliminates scalar masking by reporting separate horizontal and vertical fractional phase metrics.
    /// </summary>
    public static void AnalyzeSubpixelPhaseCoverage(
        IEnumerable<SubPixelShiftAnalysisRecord> acceptedRecords,
        out double coverageX,
        out double coverageY,
        out double grid2DCoverage)
    {
        var valid = acceptedRecords.Where(r => r.IsAccepted).ToList();
        if (valid.Count == 0)
        {
            coverageX = 0; coverageY = 0; grid2DCoverage = 0;
            return;
        }

        bool[] binsX = new bool[4];
        bool[] binsY = new bool[4];
        bool[,] grid2D = new bool[4, 4];

        foreach (var r in valid)
        {
            double fx = r.FractionalShiftX;
            double fy = r.FractionalShiftY;
            if (fx == 0 && r.ShiftX != 0) { fx = r.ShiftX - Math.Floor(r.ShiftX); if (fx < 0) fx += 1.0; }
            if (fy == 0 && r.ShiftY != 0) { fy = r.ShiftY - Math.Floor(r.ShiftY); if (fy < 0) fy += 1.0; }

            int bx = Math.Clamp((int)(fx * 4.0), 0, 3);
            int by = Math.Clamp((int)(fy * 4.0), 0, 3);

            binsX[bx] = true;
            binsY[by] = true;
            grid2D[bx, by] = true;
        }

        coverageX = binsX.Count(b => b) / 4.0;
        coverageY = binsY.Count(b => b) / 4.0;

        int pop2D = 0;
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                if (grid2D[x, y]) pop2D++;

        grid2DCoverage = pop2D / 16.0;
    }

    /// <summary>
    /// Method A: Non-uniform continuous weighted splatting (Shift-and-Add) with normalization.
    /// Accumulates subpixel-shifted samples onto the higher-resolution grid using a continuous kernel.
    /// </summary>
    public static byte[] ReconstructShiftAndAdd(
        IReadOnlyList<BurstFrameData> frames,
        IReadOnlyList<SubPixelShiftAnalysisRecord> audit,
        double scaleFactor,
        out int outWidth,
        out int outHeight)
    {
        int inW = frames[0].Width;
        int inH = frames[0].Height;
        outWidth = (int)Math.Round(inW * scaleFactor);
        outHeight = (int)Math.Round(inH * scaleFactor);

        float[] accumR = new float[outWidth * outHeight];
        float[] accumG = new float[outWidth * outHeight];
        float[] accumB = new float[outWidth * outHeight];
        float[] accumW = new float[outWidth * outHeight];

        // Footprint radius in output high-resolution pixels
        double radius = scaleFactor <= 1.5 ? 1.5 : 2.0;
        int radCeil = (int)Math.Ceiling(radius);

        for (int k = 0; k < frames.Count; k++)
        {
            var auditRec = audit[k];
            if (!auditRec.IsAccepted) continue;

            var f = frames[k];
            byte[] src = f.Bgr32Bytes;
            double sx = auditRec.ShiftX;
            double sy = auditRec.ShiftY;
            float conf = (float)auditRec.RegistrationConfidence;

            for (int iy = 0; iy < inH; iy++)
            {
                // Continuous coordinate on high-resolution grid
                double v = (iy - sy) * scaleFactor;
                int v0 = (int)Math.Floor(v);
                int inRow = iy * inW * 4;

                for (int ix = 0; ix < inW; ix++)
                {
                    double u = (ix - sx) * scaleFactor;
                    int u0 = (int)Math.Floor(u);

                    int inIdx = inRow + ix * 4;
                    float linB = SrgbToLinear(src[inIdx]);
                    float linG = SrgbToLinear(src[inIdx + 1]);
                    float linR = SrgbToLinear(src[inIdx + 2]);

                    // Splat to surrounding high-resolution grid points
                    for (int dy = -radCeil; dy <= radCeil; dy++)
                    {
                        int outY = v0 + dy;
                        if (outY < 0 || outY >= outHeight) continue;
                        double distY = Math.Abs(outY - v);
                        if (distY > radius) continue;

                        int outRow = outY * outWidth;

                        for (int dx = -radCeil; dx <= radCeil; dx++)
                        {
                            int outX = u0 + dx;
                            if (outX < 0 || outX >= outWidth) continue;
                            double distX = Math.Abs(outX - u);
                            if (distX > radius) continue;

                            double distSq = distX * distX + distY * distY;
                            if (distSq > radius * radius) continue;

                            // Smooth Wendland or Gaussian kernel
                            float weight = (float)Math.Exp(-2.0 * distSq / (radius * radius)) * conf;

                            int outIdx = outRow + outX;
                            accumB[outIdx] += linB * weight;
                            accumG[outIdx] += linG * weight;
                            accumR[outIdx] += linR * weight;
                            accumW[outIdx] += weight;
                        }
                    }
                }
            }
        }

        // Fill holes using bicubic fallback of reference frame if accumW is small
        int refIdx = audit.FirstOrDefault(a => a.IsReference)?.FrameIndex ?? 0;
        byte[] fallback = UpscaleBicubic(frames[refIdx], scaleFactor, out _, out _);

        byte[] dst = new byte[outWidth * outHeight * 4];
        for (int i = 0, p = 0; i < outWidth * outHeight; i++, p += 4)
        {
            float w = accumW[i];
            if (w > 0.05f)
            {
                dst[p] = LinearToSrgb(accumB[i] / w);
                dst[p + 1] = LinearToSrgb(accumG[i] / w);
                dst[p + 2] = LinearToSrgb(accumR[i] / w);
                dst[p + 3] = 255;
            }
            else
            {
                // Fallback to reference bicubic
                dst[p] = fallback[p];
                dst[p + 1] = fallback[p + 1];
                dst[p + 2] = fallback[p + 2];
                dst[p + 3] = 255;
            }
        }

        return dst;
    }

    /// <summary>
    /// Method B: Regularized Iterative Back-Projection (IBP).
    /// Simulates the imaging forward model (warp + PSF blur + decimation) and iteratively
    /// back-projects the residual error with edge-preserving regularization.
    /// </summary>
    public static byte[] ReconstructIterativeBackProjection(
        IReadOnlyList<BurstFrameData> frames,
        IReadOnlyList<SubPixelShiftAnalysisRecord> audit,
        double scaleFactor,
        out int outWidth,
        out int outHeight,
        int iterations = 4,
        float stepSize = 0.8f,
        float lambda = 0.04f,
        float psfSigma = 0.85f)
    {
        // Initialize with Shift-and-Add reconstruction
        byte[] currentBgr = ReconstructShiftAndAdd(frames, audit, scaleFactor, out outWidth, out outHeight);

        int totalOut = outWidth * outHeight;
        float[] curR = new float[totalOut];
        float[] curG = new float[totalOut];
        float[] curB = new float[totalOut];

        for (int i = 0, p = 0; i < totalOut; i++, p += 4)
        {
            curB[i] = SrgbToLinear(currentBgr[p]);
            curG[i] = SrgbToLinear(currentBgr[p + 1]);
            curR[i] = SrgbToLinear(currentBgr[p + 2]);
        }

        int inW = frames[0].Width;
        int inH = frames[0].Height;
        var acceptedFrames = frames.Zip(audit).Where(t => t.Second.IsAccepted).ToList();

        // 3x3 PSF Gaussian kernel
        float[] psf = CreateGaussianKernel3x3(psfSigma);

        for (int iter = 0; iter < iterations; iter++)
        {
            float[] updateR = new float[totalOut];
            float[] updateG = new float[totalOut];
            float[] updateB = new float[totalOut];
            float[] weightSum = new float[totalOut];

            foreach (var (frame, rec) in acceptedFrames)
            {
                double sx = rec.ShiftX;
                double sy = rec.ShiftY;
                byte[] actual = frame.Bgr32Bytes;

                // 1. Simulate LR acquisition from current HR estimate
                for (int iy = 0; iy < inH; iy++)
                {
                    double hrY = (iy - sy) * scaleFactor;
                    int inRow = iy * inW * 4;

                    for (int ix = 0; ix < inW; ix++)
                    {
                        double hrX = (ix - sx) * scaleFactor;

                        // Sample HR at (hrX, hrY) with PSF blur
                        SampleHrWithPsf(curB, curG, curR, outWidth, outHeight, hrX, hrY, psf, out float simB, out float simG, out float simR);

                        int inIdx = inRow + ix * 4;
                        float actB = SrgbToLinear(actual[inIdx]);
                        float actG = SrgbToLinear(actual[inIdx + 1]);
                        float actR = SrgbToLinear(actual[inIdx + 2]);

                        // Residual error in LR space
                        float errB = actB - simB;
                        float errG = actG - simG;
                        float errR = actR - simR;

                        // Back-project error to surrounding HR coordinates
                        int hx0 = (int)Math.Round(hrX);
                        int hy0 = (int)Math.Round(hrY);

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int hy = hy0 + dy;
                            if (hy < 0 || hy >= outHeight) continue;
                            int hrRow = hy * outWidth;

                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int hx = hx0 + dx;
                                if (hx < 0 || hx >= outWidth) continue;

                                float w = psf[(dy + 1) * 3 + (dx + 1)];
                                int hrIdx = hrRow + hx;

                                updateB[hrIdx] += errB * w;
                                updateG[hrIdx] += errG * w;
                                updateR[hrIdx] += errR * w;
                                weightSum[hrIdx] += w;
                            }
                        }
                    }
                }
            }

            // Apply regularized update to current estimate
            for (int i = 0; i < totalOut; i++)
            {
                float w = weightSum[i];
                if (w > 0.01f)
                {
                    // Total variation / Laplacian regularization
                    float lapB = ComputeLaplacian(curB, outWidth, outHeight, i);
                    float lapG = ComputeLaplacian(curG, outWidth, outHeight, i);
                    float lapR = ComputeLaplacian(curR, outWidth, outHeight, i);

                    curB[i] = Math.Clamp(curB[i] + stepSize * (updateB[i] / w) + lambda * lapB, 0.0f, 1.0f);
                    curG[i] = Math.Clamp(curG[i] + stepSize * (updateG[i] / w) + lambda * lapG, 0.0f, 1.0f);
                    curR[i] = Math.Clamp(curR[i] + stepSize * (updateR[i] / w) + lambda * lapR, 0.0f, 1.0f);
                }
            }
        }

        byte[] dst = new byte[totalOut * 4];
        for (int i = 0, p = 0; i < totalOut; i++, p += 4)
        {
            dst[p] = LinearToSrgb(curB[i]);
            dst[p + 1] = LinearToSrgb(curG[i]);
            dst[p + 2] = LinearToSrgb(curR[i]);
            dst[p + 3] = 255;
        }

        return dst;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SampleHrWithPsf(
        float[] bPlane, float[] gPlane, float[] rPlane,
        int w, int h, double hrX, double hrY, float[] psf,
        out float b, out float g, out float r)
    {
        int x0 = (int)Math.Round(hrX);
        int y0 = (int)Math.Round(hrY);

        b = 0; g = 0; r = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int py = Math.Clamp(y0 + dy, 0, h - 1);
            int row = py * w;
            for (int dx = -1; dx <= 1; dx++)
            {
                int px = Math.Clamp(x0 + dx, 0, w - 1);
                float pw = psf[(dy + 1) * 3 + (dx + 1)];
                int idx = row + px;
                b += bPlane[idx] * pw;
                g += gPlane[idx] * pw;
                r += rPlane[idx] * pw;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeLaplacian(float[] plane, int w, int h, int idx)
    {
        int y = idx / w;
        int x = idx % w;

        if (x <= 0 || x >= w - 1 || y <= 0 || y >= h - 1) return 0.0f;

        float center = plane[idx];
        float sum = plane[idx - 1] + plane[idx + 1] + plane[idx - w] + plane[idx + w];
        return sum - 4.0f * center;
    }

    private static float[] CreateGaussianKernel3x3(float sigma)
    {
        float[] k = new float[9];
        float sum = 0;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                float val = (float)Math.Exp(-(x * x + y * y) / (2.0f * sigma * sigma));
                k[(y + 1) * 3 + (x + 1)] = val;
                sum += val;
            }
        }
        for (int i = 0; i < 9; i++) k[i] /= sum;
        return k;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float BicubicWeight(float x)
    {
        x = Math.Abs(x);
        float a = -0.5f; // Catmull-Rom spline
        if (x <= 1.0f)
        {
            return (a + 2.0f) * x * x * x - (a + 3.0f) * x * x + 1.0f;
        }
        if (x < 2.0f)
        {
            return a * x * x * x - 5.0f * a * x * x + 8.0f * a * x - 4.0f * a;
        }
        return 0.0f;
    }

    // -------------------------------------------------------------------------------------------------
    // Quantitative Spatial Quality & Artifact Evaluation Metrics
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Computes quantitative 10-90% edge rise distance (Edge Spread Function acutance)
    /// across an edge in an ROI. A smaller rise distance indicates sharper edge bandwidth.
    /// </summary>
    public static (double RiseDistance1090Px, double OvershootPercent) MeasureEdgeAcutance(
        byte[] bgrImage, int width, int height,
        int roiX, int roiY, int roiW, int roiH,
        bool horizontalScan = true)
    {
        // Extract 1D average profile across the specified ROI
        double[] profile = horizontalScan ? new double[roiW] : new double[roiH];
        int scanLength = profile.Length;
        int crossLength = horizontalScan ? roiH : roiW;

        for (int c = 0; c < crossLength; c++)
        {
            int py = horizontalScan ? (roiY + c) : roiY;
            int px = horizontalScan ? roiX : (roiX + c);

            for (int s = 0; s < scanLength; s++)
            {
                int curX = horizontalScan ? (px + s) : px;
                int curY = horizontalScan ? py : (py + s);

                int idx = (curY * width + curX) * 4;
                // Grayscale luma BT.709
                double luma = 0.2126 * bgrImage[idx + 2] + 0.7152 * bgrImage[idx + 1] + 0.0722 * bgrImage[idx];
                profile[s] += luma;
            }
        }
        for (int s = 0; s < scanLength; s++) profile[s] /= crossLength;

        double minVal = profile.Min();
        double maxVal = profile.Max();
        double amplitude = maxVal - minVal;

        if (amplitude < 10.0)
        {
            return (0.0, 0.0); // No significant edge in ROI
        }

        double val10 = minVal + 0.10 * amplitude;
        double val90 = minVal + 0.90 * amplitude;

        double idx10 = -1;
        double idx90 = -1;

        for (int i = 0; i < scanLength - 1; i++)
        {
            if (profile[i] <= val10 && profile[i + 1] >= val10 && idx10 < 0)
            {
                idx10 = i + (val10 - profile[i]) / (profile[i + 1] - profile[i]);
            }
            if (profile[i] <= val90 && profile[i + 1] >= val90 && idx90 < 0)
            {
                idx90 = i + (val90 - profile[i]) / (profile[i + 1] - profile[i]);
            }
        }

        double riseDistance = (idx10 >= 0 && idx90 >= 0) ? Math.Abs(idx90 - idx10) : 0.0;

        // Measure overshoot: max value after transition relative to plateau
        double overshootPercent = 0.0;
        int edgeIdx = (int)Math.Round((idx10 + idx90) / 2.0);
        if (edgeIdx >= 0 && edgeIdx < scanLength - 3)
        {
            double localMax = profile.Skip(edgeIdx).Take(10).Max();
            double plateau = profile.TakeLast(5).Average();
            if (localMax > plateau)
            {
                overshootPercent = ((localMax - plateau) / amplitude) * 100.0;
            }
        }

        return (Math.Round(riseDistance, 2), Math.Round(overshootPercent, 2));
    }

    /// <summary>
    /// Computes spatial standard deviation (noise sigma) in a designated flat region.
    /// Used to verify whether super-resolution is genuinely resolving signal or amplifying noise.
    /// </summary>
    public static double MeasureFlatRegionNoiseSigma(byte[] bgrImage, int width, int height, int roiX, int roiY, int roiW, int roiH)
    {
        double sum = 0;
        double sumSq = 0;
        int count = 0;

        for (int y = roiY; y < roiY + roiH && y < height; y++)
        {
            int row = y * width * 4;
            for (int x = roiX; x < roiX + roiW && x < width; x++)
            {
                int idx = row + x * 4;
                double yVal = 0.2126 * bgrImage[idx + 2] + 0.7152 * bgrImage[idx + 1] + 0.0722 * bgrImage[idx];
                sum += yVal;
                sumSq += yVal * yVal;
                count++;
            }
        }

        if (count <= 1) return 0.0;
        double mean = sum / count;
        double variance = (sumSq - count * mean * mean) / (count - 1);
        return Math.Round(Math.Sqrt(Math.Max(0.0, variance)), 3);
    }

    /// <summary>
    /// Computes contrast modulation (Michelson contrast) across high-frequency periodic features:
    /// C = (I_max - I_min) / (I_max + I_min)
    /// </summary>
    public static double MeasureFeatureContrast(byte[] bgrImage, int width, int height, int roiX, int roiY, int roiW, int roiH)
    {
        double minVal = double.MaxValue;
        double maxVal = double.MinValue;

        for (int y = roiY; y < roiY + roiH && y < height; y++)
        {
            int row = y * width * 4;
            for (int x = roiX; x < roiX + roiW && x < width; x++)
            {
                int idx = row + x * 4;
                double luma = 0.2126 * bgrImage[idx + 2] + 0.7152 * bgrImage[idx + 1] + 0.0722 * bgrImage[idx];
                if (luma < minVal) minVal = luma;
                if (luma > maxVal) maxVal = luma;
            }
        }

        double denom = maxVal + minVal;
        return denom > 1e-3 ? Math.Round((maxVal - minVal) / denom, 3) : 0.0;
    }

    /// <summary>
    /// Computes spatial frequency response (e-SFR) and MTF50 (cycles/pixel) across a slanted edge ROI
    /// in accordance with ISO 12233 slanted-edge methodology.
    /// </summary>
    public static (double Mtf50CyclesPerPixel, double[] MtfCurve) CalculateSlantedEdgeMtf50(
        byte[] bgrImage, int width, int height,
        int roiX, int roiY, int roiW, int roiH)
    {
        // 1. Detect edge crossing per line
        double[] edgePositions = new double[roiH];
        int validRows = 0;

        for (int r = 0; r < roiH; r++)
        {
            int py = roiY + r;
            if (py >= height) break;
            int rowIdx = py * width * 4;

            double minLuma = double.MaxValue;
            double maxLuma = double.MinValue;
            double[] lineLuma = new double[roiW];

            for (int c = 0; c < roiW; c++)
            {
                int px = roiX + c;
                if (px >= width) break;
                int idx = rowIdx + px * 4;
                double y = 0.2126 * bgrImage[idx + 2] + 0.7152 * bgrImage[idx + 1] + 0.0722 * bgrImage[idx];
                lineLuma[c] = y;
                if (y < minLuma) minLuma = y;
                if (y > maxLuma) maxLuma = y;
            }

            double midLuma = (minLuma + maxLuma) / 2.0;
            double crossX = -1;

            if (maxLuma - minLuma >= 15.0)
            {
                for (int c = 0; c < roiW - 1; c++)
                {
                    if ((lineLuma[c] <= midLuma && lineLuma[c + 1] >= midLuma) ||
                        (lineLuma[c] >= midLuma && lineLuma[c + 1] <= midLuma))
                    {
                        double denom = lineLuma[c + 1] - lineLuma[c];
                        if (Math.Abs(denom) > 1e-4)
                        {
                            crossX = c + (midLuma - lineLuma[c]) / denom;
                            break;
                        }
                    }
                }
            }

            edgePositions[r] = crossX;
            if (crossX >= 0) validRows++;
        }

        if (validRows < roiH / 2)
        {
            return (0.0, Array.Empty<double>()); // No detectable edge
        }

        // 2. Linear regression to estimate edge slope x = m * y + c
        double sumY = 0, sumX = 0, sumYY = 0, sumYX = 0;
        int n = 0;
        for (int r = 0; r < roiH; r++)
        {
            if (edgePositions[r] < 0) continue;
            sumY += r;
            sumX += edgePositions[r];
            sumYY += r * r;
            sumYX += r * edgePositions[r];
            n++;
        }

        double denomFit = (n * sumYY - sumY * sumY);
        double slope = Math.Abs(denomFit) > 1e-5 ? ((n * sumYX - sumY * sumX) / denomFit) : 0;
        double intercept = Math.Abs(denomFit) > 1e-5 ? ((sumX - slope * sumY) / n) : 0;
        double normalizer = Math.Sqrt(1.0 + slope * slope);

        // 3. Project pixels into 4x oversampled Edge Spread Function (ESF) bins (0.25 px bin width)
        double binWidth = 0.25;
        int numBins = (int)(roiW / binWidth);
        double[] esfSum = new double[numBins];
        int[] esfCount = new int[numBins];

        for (int r = 0; r < roiH; r++)
        {
            int py = roiY + r;
            if (py >= height) break;
            int rowIdx = py * width * 4;
            double expectedX = slope * r + intercept;

            for (int c = 0; c < roiW; c++)
            {
                int px = roiX + c;
                if (px >= width) break;
                int idx = rowIdx + px * 4;
                double yVal = 0.2126 * bgrImage[idx + 2] + 0.7152 * bgrImage[idx + 1] + 0.0722 * bgrImage[idx];

                double dist = (c - expectedX) / normalizer;
                int bin = (int)Math.Floor((dist + (roiW / 2.0)) / binWidth);
                if (bin >= 0 && bin < numBins)
                {
                    esfSum[bin] += yVal;
                    esfCount[bin]++;
                }
            }
        }

        // Fill missing bins by interpolation
        double[] esf = new double[numBins];
        double lastVal = 0;
        for (int i = 0; i < numBins; i++)
        {
            if (esfCount[i] > 0)
            {
                esf[i] = esfSum[i] / esfCount[i];
                lastVal = esf[i];
            }
            else
            {
                esf[i] = lastVal;
            }
        }

        // 4. Line Spread Function (LSF) via finite difference derivative
        double[] lsf = new double[numBins - 2];
        for (int i = 1; i < numBins - 1; i++)
        {
            lsf[i - 1] = Math.Abs(esf[i + 1] - esf[i - 1]);
        }

        // Apply Hanning window to LSF to prevent spectral boundary leakage
        int lsfLen = lsf.Length;
        for (int i = 0; i < lsfLen; i++)
        {
            double w = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (lsfLen - 1)));
            lsf[i] *= w;
        }

        // 5. Discrete Fourier Transform (DFT) to compute MTF curve
        int numFreqs = 64;
        double[] mtf = new double[numFreqs];
        double dftDc = lsf.Sum();
        if (dftDc < 1e-4) return (0.0, Array.Empty<double>());

        double mtf50 = 0.0;
        bool found50 = false;

        for (int k = 0; k < numFreqs; k++)
        {
            // freq in cycles/pixel (sampling rate is 1.0 / binWidth = 4.0 samples/pixel)
            double freq = (k / (double)numFreqs) * 0.5; // up to Nyquist (0.5 cycles/pixel)
            double real = 0, imag = 0;

            for (int t = 0; t < lsfLen; t++)
            {
                double angle = 2.0 * Math.PI * freq * (t * binWidth);
                real += lsf[t] * Math.Cos(angle);
                imag += lsf[t] * Math.Sin(angle);
            }

            double mag = Math.Sqrt(real * real + imag * imag) / dftDc;
            mtf[k] = Math.Round(mag, 4);

            if (!found50 && mag <= 0.50 && k > 0)
            {
                double prevMag = mtf[k - 1];
                double prevFreq = ((k - 1) / (double)numFreqs) * 0.5;
                double frac = (0.50 - prevMag) / (mag - prevMag);
                mtf50 = prevFreq + frac * (freq - prevFreq);
                found50 = true;
            }
        }

        return (Math.Round(mtf50, 3), mtf);
    }

    /// <summary>
    /// Computes Peak Signal-to-Noise Ratio (PSNR) in dB between two aligned BGR32 images.
    /// </summary>
    public static double ComputePsnr(byte[] testImage, byte[] groundTruth, int width, int height)
    {
        double mse = 0;
        int totalChannels = width * height * 3;

        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int idx = row + x * 4;
                double db = testImage[idx] - groundTruth[idx];
                double dg = testImage[idx + 1] - groundTruth[idx + 1];
                double dr = testImage[idx + 2] - groundTruth[idx + 2];
                mse += (db * db + dg * dg + dr * dr);
            }
        }

        mse /= totalChannels;
        if (mse < 1e-8) return 99.99; // Identical images
        return Math.Round(10.0 * Math.Log10((255.0 * 255.0) / mse), 2);
    }

    /// <summary>
    /// Computes Structural Similarity Index (SSIM) between two aligned BGR32 images on Luma channel.
    /// </summary>
    public static double ComputeSsim(byte[] testImage, byte[] groundTruth, int width, int height)
    {
        const double C1 = 6.5025;   // (0.01 * 255)^2
        const double C2 = 58.5225;  // (0.03 * 255)^2

        int totalPixels = width * height;
        double sumX = 0, sumY = 0, sumXX = 0, sumYY = 0, sumXY = 0;

        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int idx = row + x * 4;
                double lx = 0.2126 * testImage[idx + 2] + 0.7152 * testImage[idx + 1] + 0.0722 * testImage[idx];
                double ly = 0.2126 * groundTruth[idx + 2] + 0.7152 * groundTruth[idx + 1] + 0.0722 * groundTruth[idx];

                sumX += lx;
                sumY += ly;
                sumXX += lx * lx;
                sumYY += ly * ly;
                sumXY += lx * ly;
            }
        }

        double muX = sumX / totalPixels;
        double muY = sumY / totalPixels;
        double sigmaX2 = (sumXX / totalPixels) - (muX * muX);
        double sigmaY2 = (sumYY / totalPixels) - (muY * muY);
        double sigmaXY = (sumXY / totalPixels) - (muX * muY);

        double ssim = ((2.0 * muX * muY + C1) * (2.0 * sigmaXY + C2)) /
                      ((muX * muX + muY * muY + C1) * (sigmaX2 + sigmaY2 + C2));

        return Math.Round(Math.Clamp(ssim, -1.0, 1.0), 4);
    }

    /// <summary>
    /// Encodes raw BGR32 byte buffer to PNG file.
    /// </summary>
    public static void SaveBgr32ToPng(byte[] bgrBytes, int width, int height, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var bmp = BitmapSource.Create(
            width, height, 96, 96,
            System.Windows.Media.PixelFormats.Bgr32,
            null, bgrBytes, width * 4);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        encoder.Save(fs);
    }
}
