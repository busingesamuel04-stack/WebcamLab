using System;
using System.Runtime.InteropServices;
using WebcamLab.Core.Models;

namespace WebcamLab.Capture;

/// <summary>
/// Modular, non-destructive software image processing engine.
/// Applies tonal adjustments, 3D LUT grading, and spatial filtering
/// strictly to a separate presentation buffer, preserving original camera frames.
/// </summary>
public class SoftwareLookPipeline : IDisposable
{
    private readonly object _lock = new();
    private readonly byte[] _toneLut = new byte[256];

    private IntPtr _scratchBuffer = IntPtr.Zero;
    private int _scratchBufferSize = 0;

    public SoftwareLookPipeline()
    {
        BuildToneLut(new SoftwareLookSettings());
    }

    /// <summary>
    /// Processes an input BGR32 frame into a destination BGR32 frame using the provided settings.
    /// The input buffer is NEVER modified.
    /// </summary>
    public unsafe void ProcessFrame(
        IntPtr pSourceBgr32,
        IntPtr pDestBgr32,
        int width,
        int height,
        int stride,
        SoftwareLookSettings settings,
        CubeLut? activeLut)
    {
        if (pSourceBgr32 == IntPtr.Zero || pDestBgr32 == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        int totalBytes = stride * height;

        // If software look is disabled or neutral (with no active LUT), execute fast passthrough copy
        bool hasActiveLut = settings.LutEnabled && activeLut != null && activeLut.IsLoaded;
        if (!settings.Enabled || (settings.IsNeutral() && !hasActiveLut))
        {
            Buffer.MemoryCopy((void*)pSourceBgr32, (void*)pDestBgr32, totalBytes, totalBytes);
            return;
        }

        lock (_lock)
        {
            // Stage 1: Tone & Color Curve Mapping
            BuildToneLut(settings);

            byte* pSrc = (byte*)pSourceBgr32.ToPointer();
            byte* pDst = (byte*)pDestBgr32.ToPointer();
            bool hasSaturation = Math.Abs(settings.Saturation - 1.0) > 0.001;
            float sat = (float)Math.Clamp(settings.Saturation, 0.0, 2.0);

            fixed (byte* pLut = _toneLut)
            {
                for (int y = 0; y < height; y++)
                {
                    byte* pSrcPix = pSrc + y * stride;
                    byte* pDstPix = pDst + y * stride;

                    for (int x = 0; x < width; x++)
                    {
                        byte inB = pSrcPix[0];
                        byte inG = pSrcPix[1];
                        byte inR = pSrcPix[2];

                        // Apply 1D parametric tone curve (Exposure, Contrast, Highlights, Shadows, S-Curve)
                        byte tB = pLut[inB];
                        byte tG = pLut[inG];
                        byte tR = pLut[inR];

                        if (hasSaturation)
                        {
                            // Rec.709 luma calculation
                            int yVal = (54 * tR + 183 * tG + 19 * tB + 128) >> 8;
                            if (sat <= 0.001f)
                            {
                                // True software monochrome
                                tB = (byte)yVal;
                                tG = (byte)yVal;
                                tR = (byte)yVal;
                            }
                            else
                            {
                                int adjB = yVal + (int)((tB - yVal) * sat);
                                int adjG = yVal + (int)((tG - yVal) * sat);
                                int adjR = yVal + (int)((tR - yVal) * sat);

                                tB = (byte)(adjB < 0 ? 0 : (adjB > 255 ? 255 : adjB));
                                tG = (byte)(adjG < 0 ? 0 : (adjG > 255 ? 255 : adjG));
                                tR = (byte)(adjR < 0 ? 0 : (adjR > 255 ? 255 : adjR));
                            }
                        }

                        pDstPix[0] = tB;
                        pDstPix[1] = tG;
                        pDstPix[2] = tR;
                        pDstPix[3] = 255;

                        pSrcPix += 4;
                        pDstPix += 4;
                    }
                }
            }

            // Stage 2: 3D / 1D .cube LUT Transform
            if (hasActiveLut && activeLut != null)
            {
                activeLut.ApplyInPlace(pDst, width, height, stride);
            }

            // Stage 3: Spatial Filtering (Denoising & Sharpening)
            bool doDenoise = settings.DenoiseStrength > 0.01;
            bool doSharpen = settings.SharpenStrength > 0.01;

            if (doDenoise || doSharpen)
            {
                EnsureScratchBuffer(totalBytes);
                // Copy current graded state to scratch buffer
                Buffer.MemoryCopy(pDst, (void*)_scratchBuffer, totalBytes, totalBytes);
                byte* pScratch = (byte*)_scratchBuffer.ToPointer();

                ApplySpatialFilters(
                    pScratch,
                    pDst,
                    width,
                    height,
                    stride,
                    (float)settings.DenoiseStrength,
                    (float)settings.SharpenStrength);
            }
        }
    }

    /// <summary>
    /// Precomputes the 256-byte monotonic tone curve table combining:
    /// - Software digital gain (ExposureOffset EV)
    /// - Contrast multiplier around midpoint 0.5
    /// - Shadow / Highlight zone offsets
    /// - Parametric S-curve (cubic hermite sigmoid)
    /// </summary>
    public void BuildToneLut(SoftwareLookSettings settings)
    {
        double expFactor = Math.Pow(2.0, settings.ExposureOffset);
        double contrast = settings.Contrast;
        double sCurve = Math.Clamp(settings.SCurveStrength, 0.0, 1.0);
        double shadowFactor = settings.Shadows / 100.0;
        double highlightFactor = settings.Highlights / 100.0;

        for (int i = 0; i < 256; i++)
        {
            double v = i / 255.0;

            // 1. Exposure offset (digital gain in linear light approximation)
            v *= expFactor;

            // 2. Shadows and Highlights adjustment
            if (v < 0.5)
            {
                double shadowWeight = 1.0 - 2.0 * v; // Peaks at black (1.0), drops to 0 at midtone
                v += shadowFactor * shadowWeight * 0.25;
            }
            else
            {
                double highlightWeight = 2.0 * (v - 0.5); // 0 at midtone, peaks at white (1.0)
                v += highlightFactor * highlightWeight * 0.25;
            }

            // 3. Contrast adjustment around midpoint 0.5
            v = 0.5 + (v - 0.5) * contrast;

            // 4. S-Curve contrast inflection (smoothstep: 3v^2 - 2v^3)
            if (sCurve > 0.001)
            {
                double vClamped = Math.Clamp(v, 0.0, 1.0);
                double sigmoid = vClamped * vClamped * (3.0 - 2.0 * vClamped);
                v = (1.0 - sCurve) * v + sCurve * sigmoid;
            }

            int mapped = (int)(v * 255.0 + 0.5);
            _toneLut[i] = (byte)Math.Clamp(mapped, 0, 255);
        }
    }

    private unsafe void ApplySpatialFilters(
        byte* pSrc,
        byte* pDst,
        int width,
        int height,
        int stride,
        float denoiseStr,
        float sharpenStr)
    {
        float noiseThresh = denoiseStr * 30.0f; // Max color difference considered noise
        float sharpFactor = sharpenStr * 1.5f;

        // Process interior pixels (border 1px untouched for safety)
        for (int y = 1; y < height - 1; y++)
        {
            byte* pRowPrev = pSrc + (y - 1) * stride;
            byte* pRowCurr = pSrc + y * stride;
            byte* pRowNext = pSrc + (y + 1) * stride;
            byte* pDstRow = pDst + y * stride;

            for (int x = 1; x < width - 1; x++)
            {
                int cX = x * 4;
                int leftX = (x - 1) * 4;
                int rightX = (x + 1) * 4;

                for (int c = 0; c < 3; c++) // Blue, Green, Red channels
                {
                    int center = pRowCurr[cX + c];

                    // Denoise pass: Edge-preserving 3x3 local average
                    int sum = center;
                    int count = 1;

                    if (denoiseStr > 0.01f)
                    {
                        int n0 = pRowPrev[leftX + c];
                        int n1 = pRowPrev[cX + c];
                        int n2 = pRowPrev[rightX + c];
                        int n3 = pRowCurr[leftX + c];
                        int n4 = pRowCurr[rightX + c];
                        int n5 = pRowNext[leftX + c];
                        int n6 = pRowNext[cX + c];
                        int n7 = pRowNext[rightX + c];

                        if (Math.Abs(n0 - center) < noiseThresh) { sum += n0; count++; }
                        if (Math.Abs(n1 - center) < noiseThresh) { sum += n1; count++; }
                        if (Math.Abs(n2 - center) < noiseThresh) { sum += n2; count++; }
                        if (Math.Abs(n3 - center) < noiseThresh) { sum += n3; count++; }
                        if (Math.Abs(n4 - center) < noiseThresh) { sum += n4; count++; }
                        if (Math.Abs(n5 - center) < noiseThresh) { sum += n5; count++; }
                        if (Math.Abs(n6 - center) < noiseThresh) { sum += n6; count++; }
                        if (Math.Abs(n7 - center) < noiseThresh) { sum += n7; count++; }
                    }

                    int smoothed = sum / count;

                    // Sharpen pass: Laplacian high-pass boost with halo limiter
                    int finalVal = smoothed;
                    if (sharpenStr > 0.01f)
                    {
                        int laplacian = (4 * smoothed)
                                      - pRowPrev[cX + c]
                                      - pRowNext[cX + c]
                                      - pRowCurr[leftX + c]
                                      - pRowCurr[rightX + c];

                        int boosted = smoothed + (int)(laplacian * sharpFactor);
                        // Halo limiter: do not exceed center by more than 18 code values to prevent ringing
                        finalVal = Math.Clamp(boosted, smoothed - 18, smoothed + 18);
                    }

                    pDstRow[cX + c] = (byte)Math.Clamp(finalVal, 0, 255);
                }
            }
        }
    }

    private void EnsureScratchBuffer(int requiredSize)
    {
        if (_scratchBuffer == IntPtr.Zero || _scratchBufferSize < requiredSize)
        {
            if (_scratchBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_scratchBuffer);
            }
            _scratchBuffer = Marshal.AllocHGlobal(requiredSize);
            _scratchBufferSize = requiredSize;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_scratchBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_scratchBuffer);
                _scratchBuffer = IntPtr.Zero;
                _scratchBufferSize = 0;
            }
        }
        GC.SuppressFinalize(this);
    }
}
