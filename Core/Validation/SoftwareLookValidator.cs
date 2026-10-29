using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WebcamLab.Capture;
using WebcamLab.Core.Models;

namespace WebcamLab.Core.Validation;

/// <summary>
/// Comprehensive automated mathematical verification suite for Phase 7 Software Look Pipeline:
/// 1. Neutral Identity Passthrough (0 pixel delta)
/// 2. Exposure Offset Digital Gain & Boundary Clamping
/// 3. Contrast Slope & Monotonic S-Curve Inflection
/// 4. BT.709 Chroma Saturation & Software Monochrome Precision
/// 5. .cube LUT Parser Robustness, Error Rejection & Domain Mapping
/// 6. 3D LUT Trilinear Interpolation Mathematical Precision
/// 7. Spatial Enhancement Filters (Halo Limiter & Edge Preservation)
/// 8. Buffer Isolation & Zero Source Contamination Integrity
/// 9. Preset Schema Version 2 Migration & Round-Trip Compatibility
/// </summary>
public static class SoftwareLookValidator
{
    public static List<TestResult> RunAllTests()
    {
        var results = new List<TestResult>();
        using var pipeline = new SoftwareLookPipeline();

        results.Add(TestNeutralIdentityPassthrough(pipeline));
        results.Add(TestExposureOffsetDigitalGain(pipeline));
        results.Add(TestContrastSlopeAndSCurve(pipeline));
        results.Add(TestSaturationAndMonochrome(pipeline));
        results.Add(TestCubeLutParser());
        results.Add(TestCubeLutTrilinearInterpolation());
        results.Add(TestSpatialFilters(pipeline));
        results.Add(TestBufferIsolationAndIntegrity(pipeline));
        results.Add(TestPresetSchemaV2Compatibility());

        return results;
    }

    private static TestResult TestNeutralIdentityPassthrough(SoftwareLookPipeline pipeline)
    {
        var sw = Stopwatch.StartNew();
        const int w = 192, h = 108;
        int stride = w * 4;
        int bufSize = stride * h;
        IntPtr pSrc = Marshal.AllocHGlobal(bufSize);
        IntPtr pDst = Marshal.AllocHGlobal(bufSize);

        try
        {
            unsafe
            {
                byte* s = (byte*)pSrc.ToPointer();
                // Fill with various test patterns: black, white, ramp, color bars
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * stride + x * 4;
                        s[idx + 0] = (byte)((x * 255) / w);       // B
                        s[idx + 1] = (byte)((y * 255) / h);       // G
                        s[idx + 2] = (byte)(((x + y) * 255) / (w + h)); // R
                        s[idx + 3] = 255;                         // A
                    }
                }
            }

            var neutral = new SoftwareLookSettings();
            neutral.ResetToNeutral();

            pipeline.ProcessFrame(pSrc, pDst, w, h, stride, neutral, null);
            sw.Stop();

            int mismatches = 0;
            int maxDelta = 0;
            unsafe
            {
                byte* s = (byte*)pSrc.ToPointer();
                byte* d = (byte*)pDst.ToPointer();
                for (int i = 0; i < bufSize; i++)
                {
                    int delta = Math.Abs(d[i] - s[i]);
                    if (delta > 0)
                    {
                        mismatches++;
                        if (delta > maxDelta) maxDelta = delta;
                    }
                }
            }

            bool passed = mismatches == 0 && maxDelta == 0;
            return new TestResult
            {
                Name = "1. Neutral Identity Passthrough (0 Delta Exactness)",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Sampled: {w * h} px. Mismatches: {mismatches}/{bufSize} bytes. Max Delta: {maxDelta} code values. Perfect match: {passed}."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pSrc);
            Marshal.FreeHGlobal(pDst);
        }
    }

    private static TestResult TestExposureOffsetDigitalGain(SoftwareLookPipeline pipeline)
    {
        var sw = Stopwatch.StartNew();
        var plusOneEv = new SoftwareLookSettings { ExposureOffset = 1.0 };
        var minusOneEv = new SoftwareLookSettings { ExposureOffset = -1.0 };

        pipeline.BuildToneLut(plusOneEv);
        byte[] toneLutPlus1 = (byte[])typeof(SoftwareLookPipeline)
            .GetField("_toneLut", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(pipeline)!;
        byte[] copyPlus1 = (byte[])toneLutPlus1.Clone();

        pipeline.BuildToneLut(minusOneEv);
        byte[] toneLutMinus1 = (byte[])typeof(SoftwareLookPipeline)
            .GetField("_toneLut", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(pipeline)!;
        byte[] copyMinus1 = (byte[])toneLutMinus1.Clone();
        sw.Stop();

        // At +1.0 EV, digital gain factor is 2.0x
        // 64 -> 128 (exact), 100 -> 200 (exact), 150 -> 255 (clamped)
        bool plus1Valid = Math.Abs(copyPlus1[64] - 128) <= 1
                       && Math.Abs(copyPlus1[100] - 200) <= 1
                       && copyPlus1[150] == 255
                       && copyPlus1[255] == 255;

        // At -1.0 EV, digital gain factor is 0.5x
        // 128 -> 64 (exact), 200 -> 100 (exact), 0 -> 0
        bool minus1Valid = Math.Abs(copyMinus1[128] - 64) <= 1
                        && Math.Abs(copyMinus1[200] - 100) <= 1
                        && copyMinus1[0] == 0;

        bool passed = plus1Valid && minus1Valid;
        return new TestResult
        {
            Name = "2. Exposure Offset Digital Gain & Boundary Clamping",
            Passed = passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"+1.0 EV (2.0x gain): 64->{copyPlus1[64]}, 100->{copyPlus1[100]}, 150->{copyPlus1[150]} (Clamp 255: {copyPlus1[150] == 255}). -1.0 EV (0.5x gain): 128->{copyMinus1[128]}, 200->{copyMinus1[200]}."
        };
    }

    private static TestResult TestContrastSlopeAndSCurve(SoftwareLookPipeline pipeline)
    {
        var sw = Stopwatch.StartNew();
        var highContrast = new SoftwareLookSettings { Contrast = 1.5 };
        pipeline.BuildToneLut(highContrast);
        byte[] lutContrast = (byte[])((byte[])typeof(SoftwareLookPipeline)
            .GetField("_toneLut", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(pipeline)!).Clone();

        var sCurveSettings = new SoftwareLookSettings { SCurveStrength = 0.8 };
        pipeline.BuildToneLut(sCurveSettings);
        byte[] lutSCurve = (byte[])((byte[])typeof(SoftwareLookPipeline)
            .GetField("_toneLut", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(pipeline)!).Clone();
        sw.Stop();

        // 1. Pivot point test: Midtone 128 should remain within 1 code value of 128 under pure contrast
        bool pivotValid = Math.Abs(lutContrast[128] - 128) <= 1;

        // 2. Contrast slope test: 64 should be crushed (< 64), 192 should be boosted (> 192)
        bool slopeValid = lutContrast[64] < 64 && lutContrast[192] > 192;

        // 3. S-Curve monotonicity test: each successive code value must be >= previous
        bool monotonic = true;
        for (int i = 1; i < 256; i++)
        {
            if (lutSCurve[i] < lutSCurve[i - 1])
            {
                monotonic = false;
                break;
            }
        }

        // 4. S-Curve sigmoid shape test: 64 is darkened, 192 is brightened, 0 stays 0, 255 stays 255
        bool sCurveValid = lutSCurve[64] < 64 && lutSCurve[192] > 192 && lutSCurve[0] == 0 && lutSCurve[255] == 255;

        bool passed = pivotValid && slopeValid && monotonic && sCurveValid;
        return new TestResult
        {
            Name = "3. Contrast Slope & Monotonic S-Curve Inflection",
            Passed = passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"Contrast Pivot at 128: {lutContrast[128]} (Exp: 128). Contrast Slope: 64->{lutContrast[64]}, 192->{lutContrast[192]}. S-Curve Monotonic: {monotonic}, 64->{lutSCurve[64]}, 192->{lutSCurve[192]}."
        };
    }

    private static TestResult TestSaturationAndMonochrome(SoftwareLookPipeline pipeline)
    {
        var sw = Stopwatch.StartNew();
        const int w = 4, h = 1;
        int stride = w * 4;
        IntPtr pSrc = Marshal.AllocHGlobal(stride * h);
        IntPtr pDst = Marshal.AllocHGlobal(stride * h);

        try
        {
            unsafe
            {
                byte* s = (byte*)pSrc.ToPointer();
                // Pixel 0: Pure Red (B=0, G=0, R=255)
                s[0] = 0; s[1] = 0; s[2] = 255; s[3] = 255;
                // Pixel 1: Pure Green (B=0, G=255, R=0)
                s[4] = 0; s[5] = 255; s[6] = 0; s[7] = 255;
                // Pixel 2: Pure Blue (B=255, G=0, R=0)
                s[8] = 255; s[9] = 0; s[10] = 0; s[11] = 255;
                // Pixel 3: Skin Tone Test (B=100, G=140, R=210)
                s[12] = 100; s[13] = 140; s[14] = 210; s[15] = 255;
            }

            var monoSettings = new SoftwareLookSettings { Saturation = 0.0 };
            pipeline.ProcessFrame(pSrc, pDst, w, h, stride, monoSettings, null);
            sw.Stop();

            byte rY_B, rY_G, rY_R;
            byte gY_B, gY_G, gY_R;
            byte bY_B, bY_G, bY_R;
            byte skY_B, skY_G, skY_R;

            unsafe
            {
                byte* d = (byte*)pDst.ToPointer();
                rY_B = d[0]; rY_G = d[1]; rY_R = d[2];
                gY_B = d[4]; gY_G = d[5]; gY_R = d[6];
                bY_B = d[8]; bY_G = d[9]; bY_R = d[10];
                skY_B = d[12]; skY_G = d[13]; skY_R = d[14];
            }

            // Rec.709 integer formula: Y = (54*R + 183*G + 19*B + 128) >> 8
            // Red: (54*255 + 128) >> 8 = 13898 >> 8 = 54
            // Green: (183*255 + 128) >> 8 = 46793 >> 8 = 182 or 183
            // Blue: (19*255 + 128) >> 8 = 4973 >> 8 = 19
            bool rMono = (rY_B == rY_G && rY_G == rY_R) && Math.Abs(rY_R - 54) <= 1;
            bool gMono = (gY_B == gY_G && gY_G == gY_R) && Math.Abs(gY_R - 183) <= 1;
            bool bMono = (bY_B == bY_G && bY_G == bY_R) && Math.Abs(bY_R - 19) <= 1;
            bool skMono = (skY_B == skY_G && skY_G == skY_R);

            bool passed = rMono && gMono && bMono && skMono;
            return new TestResult
            {
                Name = "4. BT.709 Chroma Saturation & Software Monochrome Precision",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Pure Red->({rY_R},{rY_G},{rY_B}) [Exp: 54]. Pure Green->({gY_R},{gY_G},{gY_B}) [Exp: 183]. Pure Blue->({bY_R},{bY_G},{bY_B}) [Exp: 19]. Skin Tone->({skY_R},{skY_G},{skY_B}). All R=G=B: {passed}."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pSrc);
            Marshal.FreeHGlobal(pDst);
        }
    }

    private static TestResult TestCubeLutParser()
    {
        var sw = Stopwatch.StartNew();

        // 1. Valid 3D LUT string
        string valid3dCube = @"# Valid Test 3D LUT
TITLE ""Test3D""
LUT_3D_SIZE 2
DOMAIN_MIN 0.0 0.0 0.0
DOMAIN_MAX 1.0 1.0 1.0

0.0 0.0 0.0
1.0 0.0 0.0
0.0 1.0 0.0
1.0 1.0 0.0
0.0 0.0 1.0
1.0 0.0 1.0
0.0 1.0 1.0
1.0 1.0 1.0
";
        var lut3d = CubeLut.Parse(valid3dCube, "valid3d.cube");
        bool valid3dPass = lut3d.Type == CubeLutType.Lut3D && lut3d.Size == 2 && lut3d.Title == "Test3D";

        // 2. Valid 1D LUT string
        string valid1dCube = @"# Valid Test 1D LUT
TITLE ""Test1D""
LUT_1D_SIZE 3
DOMAIN_MIN 0.0 0.0 0.0
DOMAIN_MAX 1.0 1.0 1.0

0.0 0.0 0.0
0.5 0.5 0.5
1.0 1.0 1.0
";
        var lut1d = CubeLut.Parse(valid1dCube, "valid1d.cube");
        bool valid1dPass = lut1d.Type == CubeLutType.Lut1D && lut1d.Size == 3;

        // 3. Reject entry count mismatch (declared 2^3=8, but only 4 provided)
        string malformedCountCube = @"TITLE ""BadCount""
LUT_3D_SIZE 2
0.0 0.0 0.0
1.0 0.0 0.0
0.0 1.0 0.0
1.0 1.0 0.0
";
        bool caughtCountError = false;
        try
        {
            CubeLut.Parse(malformedCountCube, "bad_count.cube");
        }
        catch (FormatException)
        {
            caughtCountError = true;
        }

        // 4. Reject malformed floats
        string malformedFloatCube = @"LUT_1D_SIZE 2
0.0 0.0 0.0
1.0 INVALID_FLOAT 1.0
";
        bool caughtFloatError = false;
        try
        {
            CubeLut.Parse(malformedFloatCube, "bad_float.cube");
        }
        catch (FormatException)
        {
            caughtFloatError = true;
        }

        sw.Stop();
        bool passed = valid3dPass && valid1dPass && caughtCountError && caughtFloatError;
        return new TestResult
        {
            Name = "5. .cube LUT Parser Robustness & Error Rejection",
            Passed = passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"Valid 3D Parsed: {valid3dPass}. Valid 1D Parsed: {valid1dPass}. Entry Count Mismatch Caught: {caughtCountError}. Invalid Float Caught: {caughtFloatError}."
        };
    }

    private static TestResult TestCubeLutTrilinearInterpolation()
    {
        var sw = Stopwatch.StartNew();
        var idLut = CubeLut.CreateIdentity3D(17, "Identity 17");

        int maxDelta = 0;
        double sumDelta = 0;
        int testCount = 0;

        // Test all 256 gray steps
        for (int gray = 0; gray < 256; gray++)
        {
            var (r, g, b) = idLut.Sample((byte)gray, (byte)gray, (byte)gray);
            int deltaR = Math.Abs(r - gray);
            int deltaG = Math.Abs(g - gray);
            int deltaB = Math.Abs(b - gray);
            int dMax = Math.Max(deltaR, Math.Max(deltaG, deltaB));
            if (dMax > maxDelta) maxDelta = dMax;
            sumDelta += deltaR + deltaG + deltaB;
            testCount += 3;
        }

        // Test fractional off-grid points
        byte[][] offGridTriplets =
        [
            [15, 87, 213],
            [63, 127, 191],
            [1, 128, 254],
            [99, 44, 188],
            [240, 10, 150]
        ];

        foreach (var t in offGridTriplets)
        {
            var (r, g, b) = idLut.Sample(t[0], t[1], t[2]);
            int dR = Math.Abs(r - t[0]);
            int dG = Math.Abs(g - t[1]);
            int dB = Math.Abs(b - t[2]);
            int dMax = Math.Max(dR, Math.Max(dG, dB));
            if (dMax > maxDelta) maxDelta = dMax;
            sumDelta += dR + dG + dB;
            testCount += 3;
        }

        sw.Stop();
        double avgDelta = sumDelta / testCount;
        // Trilinear interpolation on 17x17x17 identity should have max integer rounding delta <= 1 code value
        bool passed = maxDelta <= 1 && avgDelta < 0.2;

        return new TestResult
        {
            Name = "6. 3D LUT Trilinear Interpolation Mathematical Precision",
            Passed = passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"Identity 17x17x17 Tested across {testCount / 3} RGB points. Max Delta: {maxDelta} code values (Target <= 1). Mean Absolute Error: {avgDelta:F4} code values."
        };
    }

    private static TestResult TestSpatialFilters(SoftwareLookPipeline pipeline)
    {
        var sw = Stopwatch.StartNew();
        const int w = 64, h = 64;
        int stride = w * 4;
        int totalBytes = stride * h;
        IntPtr pSrc = Marshal.AllocHGlobal(totalBytes);
        IntPtr pDst = Marshal.AllocHGlobal(totalBytes);

        try
        {
            unsafe
            {
                byte* s = (byte*)pSrc.ToPointer();
                // Create sharp vertical step edge: left half 100, right half 150
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * stride + x * 4;
                        byte val = (byte)(x < 32 ? 100 : 150);
                        s[idx + 0] = val;
                        s[idx + 1] = val;
                        s[idx + 2] = val;
                        s[idx + 3] = 255;
                    }
                }
            }

            var sharpenSettings = new SoftwareLookSettings { SharpenStrength = 0.5 };
            pipeline.ProcessFrame(pSrc, pDst, w, h, stride, sharpenSettings, null);
            sw.Stop();

            byte edgeLeft, edgeRight, flatLeft, flatRight;
            unsafe
            {
                byte* d = (byte*)pDst.ToPointer();
                // x=31 is left edge, x=32 is right edge
                edgeLeft = d[32 * stride + 31 * 4];
                edgeRight = d[32 * stride + 32 * 4];
                // Flat area far from edge: x=10 and x=50
                flatLeft = d[32 * stride + 10 * 4];
                flatRight = d[32 * stride + 50 * 4];
            }

            // Unsharp masking: edgeLeft (< 100) and edgeRight (> 150)
            bool edgeSharpened = edgeLeft < 100 && edgeRight > 150;
            // Halo limiter check: edgeLeft must not overshoot below 100 - 18 = 82
            bool haloConstrained = edgeLeft >= 82 && edgeRight <= 168;
            // Flat areas far from edge must remain unchanged (100 and 150)
            bool flatPreserved = flatLeft == 100 && flatRight == 150;

            bool passed = edgeSharpened && haloConstrained && flatPreserved;
            return new TestResult
            {
                Name = "7. Spatial Enhancement Filters (Halo Limiter & Edge Preservation)",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Edge Left: 100->{edgeLeft} (Sharpened: {edgeLeft < 100}). Edge Right: 150->{edgeRight} (Sharpened: {edgeRight > 150}). Halo Limiter within ±18: {haloConstrained}. Flat Area Unaltered: {flatPreserved}."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pSrc);
            Marshal.FreeHGlobal(pDst);
        }
    }

    private static TestResult TestBufferIsolationAndIntegrity(SoftwareLookPipeline pipeline)
    {
        var sw = Stopwatch.StartNew();
        const int w = 320, h = 240;
        int stride = w * 4;
        int totalBytes = stride * h;
        IntPtr pSrc = Marshal.AllocHGlobal(totalBytes);
        IntPtr pDst = Marshal.AllocHGlobal(totalBytes);
        byte[] originalSourceCopy = new byte[totalBytes];

        try
        {
            var rand = new Random(42);
            unsafe
            {
                byte* s = (byte*)pSrc.ToPointer();
                for (int i = 0; i < totalBytes; i++)
                {
                    byte b = (byte)rand.Next(256);
                    s[i] = b;
                    originalSourceCopy[i] = b;
                }
            }

            // Run aggressive look processing (contrast 1.5, saturation 0.0, sharpen 0.8)
            var aggressiveLook = new SoftwareLookSettings
            {
                ExposureOffset = 0.5,
                Contrast = 1.4,
                Saturation = 0.0,
                SCurveStrength = 0.5,
                SharpenStrength = 0.8
            };

            pipeline.ProcessFrame(pSrc, pDst, w, h, stride, aggressiveLook, null);
            sw.Stop();

            // Verify pSrc is 100% untouched bit-for-bit
            int srcCorruptions = 0;
            unsafe
            {
                byte* s = (byte*)pSrc.ToPointer();
                for (int i = 0; i < totalBytes; i++)
                {
                    if (s[i] != originalSourceCopy[i])
                    {
                        srcCorruptions++;
                    }
                }
            }

            // Verify pDst received transformed data
            int dstChanges = 0;
            unsafe
            {
                byte* d = (byte*)pDst.ToPointer();
                for (int i = 0; i < totalBytes; i++)
                {
                    if (d[i] != originalSourceCopy[i])
                    {
                        dstChanges++;
                    }
                }
            }

            bool passed = srcCorruptions == 0 && dstChanges > (totalBytes / 2);
            return new TestResult
            {
                Name = "8. Buffer Isolation & Zero Source Contamination Integrity",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Source Buffer Corruptions: {srcCorruptions}/{totalBytes} bytes. Destination Buffer Graded Bytes: {dstChanges}/{totalBytes}. Zero Contamination Confirmed: {srcCorruptions == 0}."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pSrc);
            Marshal.FreeHGlobal(pDst);
        }
    }

    private static TestResult TestPresetSchemaV2Compatibility()
    {
        var sw = Stopwatch.StartNew();

        // 1. Legacy Schema Version 1 JSON (no SoftwareLook field)
        string legacyV1Json = @"{
  ""id"": ""legacy-test-preset"",
  ""name"": ""Legacy V1 Profile"",
  ""description"": ""Phase 6 preset without software look"",
  ""isBuiltIn"": false,
  ""schemaVersion"": 1,
  ""hardwareControls"": [
    {
      ""propertyName"": ""Brightness"",
      ""propertyType"": 0,
      ""propertyId"": 0,
      ""value"": 5,
      ""isAuto"": false
    }
  ]
}";

        var presetService = new PresetService();
        var v1Preset = JsonSerializer.Deserialize<CapturePreset>(legacyV1Json, PresetService.JsonOptions);
        bool v1Loaded = v1Preset != null && v1Preset.Name == "Legacy V1 Profile";
        bool v1Migrated = v1Preset != null && v1Preset.SoftwareLook != null && v1Preset.SoftwareLook.IsNeutral();

        // 2. Schema Version 2 Round-Trip Serialization
        var v2Preset = new CapturePreset
        {
            Id = "v2-test-preset",
            Name = "Cinema Warm V2",
            Description = "Phase 7 preset with 3D LUT and tone curve",
            SchemaVersion = 2,
            SoftwareLook = new SoftwareLookSettings
            {
                LookName = "Warm Amber Cinema",
                Enabled = true,
                ExposureOffset = 0.25,
                Contrast = 1.15,
                Saturation = 1.05,
                SCurveStrength = 0.35,
                LutFileName = "film_warmth.cube",
                LutEnabled = true,
                SharpenStrength = 0.20
            }
        };

        string serializedV2 = JsonSerializer.Serialize(v2Preset, PresetService.JsonOptions);
        var deserializedV2 = JsonSerializer.Deserialize<CapturePreset>(serializedV2, PresetService.JsonOptions);

        bool v2Matches = deserializedV2 != null
                      && deserializedV2.SchemaVersion == 2
                      && deserializedV2.SoftwareLook != null
                      && deserializedV2.SoftwareLook.LookName == "Warm Amber Cinema"
                      && Math.Abs(deserializedV2.SoftwareLook.ExposureOffset - 0.25) < 0.001
                      && deserializedV2.SoftwareLook.LutFileName == "film_warmth.cube"
                      && deserializedV2.SoftwareLook.LutEnabled;

        sw.Stop();
        bool passed = v1Loaded && v1Migrated && v2Matches;
        return new TestResult
        {
            Name = "9. Preset Schema Version 2 Migration & Round-Trip Compatibility",
            Passed = passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"Legacy V1 Auto-Migration to Neutral Look: {v1Migrated}. Schema V2 Full Round-Trip Fidelity: {v2Matches}."
        };
    }
}
