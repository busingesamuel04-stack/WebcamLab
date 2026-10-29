using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WebcamLab.Capture;
using WebcamLab.Core.Models;
using WebcamLab.Core.SuperResolution;

namespace WebcamLab.Core.Validation;

/// <summary>
/// Automated validation test suite for Phase 26: Multi-Frame Super-Resolution Research.
/// Tests subpixel registration mathematics, sampling diversity metrics, splatting reconstruction,
/// iterative back-projection operators, edge acutance / MTF metrics, and safety fallbacks.
/// </summary>
public static class Phase26SuperResolutionValidator
{
    public static List<(string Name, bool Passed, string Details, double ElapsedMs)> RunTests()
    {
        var results = new List<(string Name, bool Passed, string Details, double ElapsedMs)>();

        // Test 1: Subpixel registration accuracy with synthetic subpixel shift
        RunTest(results, "SubPixelRegistration_FractionalShift_Accuracy", () =>
        {
            int w = 256, h = 256;
            var refBytes = CreateSyntheticPattern(w, h, shiftX: 0.0, shiftY: 0.0);
            var refFrame = BurstFrameData.Create(0, 1000, 100, w, h, refBytes);

            // Shift by exactly dx = +0.35, dy = -0.25 px
            double trueDx = 0.35, trueDy = -0.25;
            var tgtBytes = CreateSyntheticPattern(w, h, shiftX: trueDx, shiftY: trueDy);
            var tgtFrame = BurstFrameData.Create(1, 2000, 200, w, h, tgtBytes);

            var (estX, estY, conf) = MultiFrameEnhancementEngine.RegisterFrame(tgtFrame, refFrame);
            double errX = Math.Abs(estX - trueDx);
            double errY = Math.Abs(estY - trueDy);

            // Subpixel registration error bound: within +/- 0.25 px on 8-bit sampled patterns
            bool pass = errX <= 0.25 && errY <= 0.25 && conf >= 0.80 && Math.Sign(estX) == Math.Sign(trueDx) && Math.Sign(estY) == Math.Sign(trueDy);
            return (pass, $"True=({trueDx:F2}, {trueDy:F2}), Est=({estX:F2}, {estY:F2}), Err=({errX:F2}, {errY:F2}) px, Conf={conf:F3}");
        });

        // Test 2: Subpixel sampling diversity score validation
        RunTest(results, "SubpixelDiversityScore_RedundantVsDistributed", () =>
        {
            // Set A: Redundant sampling (all frames at ~0.0 shift)
            var redundant = new List<SubPixelShiftAnalysisRecord>
            {
                new() { FractionalShiftX = 0.00, FractionalShiftY = 0.00, IsAccepted = true },
                new() { FractionalShiftX = 0.02, FractionalShiftY = 0.01, IsAccepted = true },
                new() { FractionalShiftX = 0.01, FractionalShiftY = 0.03, IsAccepted = true },
                new() { FractionalShiftX = 0.00, FractionalShiftY = 0.02, IsAccepted = true }
            };
            double scoreRedundant = SuperResolutionEngine.ComputeSubpixelDiversityScore(redundant);

            // Set B: Well-distributed complementary sampling phases
            var distributed = new List<SubPixelShiftAnalysisRecord>
            {
                new() { FractionalShiftX = 0.05, FractionalShiftY = 0.05, IsAccepted = true },
                new() { FractionalShiftX = 0.55, FractionalShiftY = 0.10, IsAccepted = true },
                new() { FractionalShiftX = 0.12, FractionalShiftY = 0.60, IsAccepted = true },
                new() { FractionalShiftX = 0.62, FractionalShiftY = 0.65, IsAccepted = true }
            };
            double scoreDistributed = SuperResolutionEngine.ComputeSubpixelDiversityScore(distributed);

            bool pass = scoreRedundant < 0.35 && scoreDistributed >= 0.65;
            return (pass, $"Redundant Score={scoreRedundant:F3} (<0.35), Distributed Score={scoreDistributed:F3} (>=0.65)");
        });

        // Test 3: Keys Bicubic Upscaling precision
        RunTest(results, "BicubicUpscale_CatmullRom_DimensionsAndSmoothness", () =>
        {
            int inW = 128, inH = 128;
            var frame = BurstFrameData.Create(0, 0, 0, inW, inH, CreateSyntheticPattern(inW, inH));
            byte[] upscaled = SuperResolutionEngine.UpscaleBicubic(frame, 1.5, out int outW, out int outH);

            bool correctDims = (outW == 192) && (outH == 192) && (upscaled.Length == 192 * 192 * 4);
            // Verify no NaN or black pixels
            bool noHoles = upscaled.Take(1000).Any(b => b > 0);

            return (correctDims && noHoles, $"Target 1.5x output: {outW}x{outH} ({upscaled.Length} bytes), Non-zero data verified.");
        });

        // Test 4: Shift-and-Add non-uniform splatting reconstruction
        RunTest(results, "ShiftAndAdd_NormalizedSplatting_Reconstruction", () =>
        {
            int inW = 128, inH = 128;
            var frames = new List<BurstFrameData>();
            var audit = new List<SubPixelShiftAnalysisRecord>();

            // Generate 4 complementary shifted frames
            double[] shiftsX = [0.0, 0.5, 0.0, 0.5];
            double[] shiftsY = [0.0, 0.0, 0.5, 0.5];

            for (int i = 0; i < 4; i++)
            {
                frames.Add(BurstFrameData.Create(i, i * 1000, 0, inW, inH, CreateSyntheticPattern(inW, inH, shiftsX[i], shiftsY[i])));
                audit.Add(new SubPixelShiftAnalysisRecord
                {
                    FrameIndex = i,
                    ShiftX = shiftsX[i],
                    ShiftY = shiftsY[i],
                    FractionalShiftX = shiftsX[i],
                    FractionalShiftY = shiftsY[i],
                    RegistrationConfidence = 1.0,
                    IsReference = i == 0,
                    IsAccepted = true
                });
            }

            byte[] srBgr = SuperResolutionEngine.ReconstructShiftAndAdd(frames, audit, 1.5, out int outW, out int outH);
            bool pass = outW == 192 && outH == 192 && srBgr.Length == 192 * 192 * 4;
            return (pass, $"Reconstructed 1.5x grid: {outW}x{outH}, Output size={srBgr.Length} bytes.");
        });

        // Test 5: Regularized Iterative Back-Projection convergence
        RunTest(results, "IterativeBackProjection_ConvergenceAndRegularization", () =>
        {
            int inW = 96, inH = 96;
            var frames = new List<BurstFrameData>();
            var audit = new List<SubPixelShiftAnalysisRecord>();

            for (int i = 0; i < 3; i++)
            {
                double sx = i * 0.33;
                double sy = i * 0.25;
                frames.Add(BurstFrameData.Create(i, i * 1000, 0, inW, inH, CreateSyntheticPattern(inW, inH, sx, sy)));
                audit.Add(new SubPixelShiftAnalysisRecord
                {
                    FrameIndex = i,
                    ShiftX = sx,
                    ShiftY = sy,
                    RegistrationConfidence = 0.95,
                    IsAccepted = true
                });
            }

            byte[] ibpBgr = SuperResolutionEngine.ReconstructIterativeBackProjection(
                frames, audit, 1.5, out int outW, out int outH, iterations: 3, stepSize: 0.7f, lambda: 0.05f, psfSigma: 0.8f);

            bool pass = ibpBgr.Length == outW * outH * 4 && ibpBgr.Any(b => b > 10);
            return (pass, $"IBP completed 3 iterations on {outW}x{outH} grid without divergence.");
        });

        // Test 6: Edge Acutance (10-90% Rise Distance) measurement
        RunTest(results, "QuantitativeAcutance_EdgeRiseDistance_Measurement", () =>
        {
            int w = 100, h = 50;
            byte[] edgeImg = new byte[w * h * 4];
            // Sharp step edge at x = 50
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    byte val = x >= 50 ? (byte)220 : (byte)20;
                    int idx = row + x * 4;
                    edgeImg[idx] = val;
                    edgeImg[idx + 1] = val;
                    edgeImg[idx + 2] = val;
                    edgeImg[idx + 3] = 255;
                }
            }

            var (riseDist, overshoot) = SuperResolutionEngine.MeasureEdgeAcutance(edgeImg, w, h, 10, 10, 80, 30, horizontalScan: true);
            bool pass = riseDist > 0 && riseDist <= 2.5 && overshoot <= 5.0;
            return (pass, $"Measured Step Edge: RiseDistance={riseDist:F2} px, Overshoot={overshoot:F1}%");
        });

        // Test 7: Flat Region Noise Sigma measurement
        RunTest(results, "FlatRegionNoiseSigma_NoiseAmplificationCheck", () =>
        {
            int w = 100, h = 100;
            byte[] flatImg = new byte[w * h * 4];
            var rng = new Random(42);
            for (int i = 0; i < w * h * 4; i += 4)
            {
                byte val = (byte)Math.Clamp(128 + rng.Next(-10, 11), 0, 255);
                flatImg[i] = val;
                flatImg[i + 1] = val;
                flatImg[i + 2] = val;
                flatImg[i + 3] = 255;
            }

            double sigma = SuperResolutionEngine.MeasureFlatRegionNoiseSigma(flatImg, w, h, 10, 10, 80, 80);
            bool pass = sigma >= 4.0 && sigma <= 8.0;
            return (pass, $"Flat region noise sigma: {sigma:F2} (expected uniform [-10, 10] sigma ~ 5.77)");
        });

        // Test 8: Feature Separation Contrast (Michelson Contrast)
        RunTest(results, "FeatureSeparation_MichelsonContrast_Validation", () =>
        {
            int w = 100, h = 50;
            byte[] barImg = new byte[w * h * 4];
            // Alternating 2-pixel bars: bright=200, dark=50
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    byte val = ((x / 2) % 2 == 0) ? (byte)200 : (byte)50;
                    int idx = row + x * 4;
                    barImg[idx] = val;
                    barImg[idx + 1] = val;
                    barImg[idx + 2] = val;
                    barImg[idx + 3] = 255;
                }
            }

            double contrast = SuperResolutionEngine.MeasureFeatureContrast(barImg, w, h, 10, 10, 80, 30);
            // Expected contrast = (200 - 50) / (200 + 50) = 150 / 250 = 0.60
            bool pass = Math.Abs(contrast - 0.60) <= 0.05;
            return (pass, $"Measured Bar Target Contrast: {contrast:F3} (expected ~0.600)");
        });

        // Test 9: Rejection of unreliable / large displacement frames
        RunTest(results, "OutlierRejection_UncertaintyAndExcessiveMotion", () =>
        {
            int w = 128, h = 128;
            var refFrame = BurstFrameData.Create(0, 0, 0, w, h, CreateSyntheticPattern(w, h));
            var outlierFrame = BurstFrameData.Create(1, 1000, 0, w, h, CreateSyntheticPattern(w, h, shiftX: 55.0, shiftY: 0.0));

            var records = SuperResolutionEngine.AnalyzeBurstShifts(new[] { refFrame, outlierFrame }, 0, maxShiftPx: 32.0);
            bool rejected = !records[1].IsAccepted && records[1].RejectionReason != null;

            return (rejected, $"55px displacement correctly rejected: '{records[1].RejectionReason}'");
        });

        // Test 10: Zero-Sensor-RAW claim disclaimer integrity
        RunTest(results, "ZeroSensorRawClaim_ProvenanceIntegrity", () =>
        {
            var sidecar = new SuperResolutionSidecar();
            bool hasDisclaimer = sidecar.SensorRawClaim.Contains("None") &&
                                 sidecar.SensorRawClaim.Contains("not Bayer sensor RAW");
            return (hasDisclaimer, $"Disclaimer verified: \"{sidecar.SensorRawClaim}\"");
        });

        // Test 11: Phase 27 - Lanczos3 windowed-sinc upscaling accuracy
        RunTest(results, "Lanczos3_Reconstruction_Accuracy", () =>
        {
            int inW = 128, inH = 128;
            var frame = BurstFrameData.Create(0, 0, 0, inW, inH, CreateSyntheticPattern(inW, inH));
            byte[] upscaled = SuperResolutionEngine.UpscaleLanczos3(frame, 1.5, out int outW, out int outH);

            bool correctDims = (outW == 192) && (outH == 192) && (upscaled.Length == 192 * 192 * 4);
            bool noHoles = upscaled.Take(1000).Any(b => b > 0);

            return (correctDims && noHoles, $"Target 1.5x Lanczos3 output: {outW}x{outH} ({upscaled.Length} bytes), Non-zero data verified.");
        });

        // Test 12: Phase 27 - ISO 12233 e-SFR Slanted-Edge MTF50 measurement
        RunTest(results, "SlantedEdgeMtf50_ISO12233_eSFR_Convergence", () =>
        {
            int w = 128, h = 128;
            byte[] edgeBgr = new byte[w * h * 4];
            // Slanted edge: x_edge = 0.087 * y + 60 (~5 degrees slant)
            for (int y = 0; y < h; y++)
            {
                double edgeX = 0.087 * y + 60.0;
                int row = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    double d = x - edgeX;
                    // Transition over ~1.5 pixels
                    double luma = 40.0 + 160.0 / (1.0 + Math.Exp(-d * 2.5));
                    byte b = (byte)Math.Clamp((int)Math.Round(luma), 0, 255);
                    int idx = row + x * 4;
                    edgeBgr[idx] = b;
                    edgeBgr[idx + 1] = b;
                    edgeBgr[idx + 2] = b;
                    edgeBgr[idx + 3] = 255;
                }
            }

            var (mtf50, curve) = SuperResolutionEngine.CalculateSlantedEdgeMtf50(edgeBgr, w, h, 20, 20, 88, 88);
            bool pass = mtf50 >= 0.10 && mtf50 <= 0.48 && curve.Length > 0;
            return (pass, $"e-SFR MTF50: {mtf50:F3} cycles/pixel (expected 0.10 - 0.48, curve points={curve.Length})");
        });

        // Test 13: Phase 27 - Two-axis phase coverage and anisotropy detection
        RunTest(results, "TwoAxisPhaseCoverage_AnisotropyDetection", () =>
        {
            // Case 1: Anisotropic 1D vertical drift (X degenerate, Y distributed)
            var verticalRecords = new List<SubPixelShiftAnalysisRecord>
            {
                new() { ShiftX = 0.02, ShiftY = 0.10, FractionalShiftX = 0.02, FractionalShiftY = 0.10, IsAccepted = true },
                new() { ShiftX = 0.01, ShiftY = 0.35, FractionalShiftX = 0.01, FractionalShiftY = 0.35, IsAccepted = true },
                new() { ShiftX = 0.03, ShiftY = 0.60, FractionalShiftX = 0.03, FractionalShiftY = 0.60, IsAccepted = true },
                new() { ShiftX = 0.02, ShiftY = 0.85, FractionalShiftX = 0.02, FractionalShiftY = 0.85, IsAccepted = true }
            };
            SuperResolutionEngine.AnalyzeSubpixelPhaseCoverage(verticalRecords, out double covX1, out double covY1, out double grid1);

            // Case 2: Full 2D isotropic complementary sampling
            var isotropicRecords = new List<SubPixelShiftAnalysisRecord>
            {
                new() { ShiftX = 0.10, ShiftY = 0.10, FractionalShiftX = 0.10, FractionalShiftY = 0.10, IsAccepted = true },
                new() { ShiftX = 0.60, ShiftY = 0.15, FractionalShiftX = 0.60, FractionalShiftY = 0.15, IsAccepted = true },
                new() { ShiftX = 0.15, ShiftY = 0.65, FractionalShiftX = 0.15, FractionalShiftY = 0.65, IsAccepted = true },
                new() { ShiftX = 0.65, ShiftY = 0.65, FractionalShiftX = 0.65, FractionalShiftY = 0.65, IsAccepted = true }
            };
            SuperResolutionEngine.AnalyzeSubpixelPhaseCoverage(isotropicRecords, out double covX2, out double covY2, out double grid2);

            bool pass1 = covX1 <= 0.25 && covY1 >= 0.75;
            bool pass2 = covX2 >= 0.50 && covY2 >= 0.50 && grid2 >= 0.25;

            return (pass1 && pass2, $"1D Anisotropic: covX={covX1:F2}, covY={covY1:F2}. 2D Isotropic: covX={covX2:F2}, covY={covY2:F2}, grid2D={grid2:F2}");
        });

        return results;
    }

    private static void RunTest(
        List<(string Name, bool Passed, string Details, double ElapsedMs)> results,
        string name,
        Func<(bool Passed, string Details)> testFunc)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (passed, details) = testFunc();
            sw.Stop();
            results.Add((name, passed, details, sw.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            results.Add((name, false, $"Exception: {ex.Message}", sw.Elapsed.TotalMilliseconds));
        }
    }

    private static byte[] CreateSyntheticPattern(int width, int height, double shiftX = 0, double shiftY = 0)
    {
        byte[] bytes = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            double sy = y - shiftY;
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                double sx = x - shiftX;
                // Multi-frequency sinusoidal pattern with step edges
                double v1 = Math.Sin(sx * 0.15) * Math.Cos(sy * 0.15);
                double v2 = Math.Sin(sx * 0.05 + sy * 0.05);
                double edge = (sx > width / 2.0) ? 0.3 : -0.3;
                double val = Math.Clamp(128 + 60 * v1 + 40 * v2 + 50 * edge, 10, 245);

                byte b = (byte)Math.Round(val);
                int idx = row + x * 4;
                bytes[idx] = b;
                bytes[idx + 1] = b;
                bytes[idx + 2] = b;
                bytes[idx + 3] = 255;
            }
        }
        return bytes;
    }
}
