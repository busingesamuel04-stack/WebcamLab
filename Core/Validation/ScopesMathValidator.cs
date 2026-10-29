using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WebcamLab.Capture;

namespace WebcamLab.Core.Validation;

public class TestResult
{
    public string Name { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Details { get; init; } = string.Empty;
    public double ExecutionMs { get; init; }
}

public static class ScopesMathValidator
{
    public static List<TestResult> RunAllTests()
    {
        var results = new List<TestResult>();
        var analyzer = new ImageAnalyzer();

        results.Add(TestSolidBlack(analyzer));
        results.Add(TestSolidWhite(analyzer));
        results.Add(TestNeutralGrayRamp(analyzer));
        results.Add(TestRgbPrimaries(analyzer));
        results.Add(TestCodeValueBoundaries(analyzer));
        results.Add(TestCalibratedProportions(analyzer));
        results.Add(TestYuy2Equivalence(analyzer));
        results.Add(TestZebraPerformanceAndCorrectness(analyzer));

        return results;
    }

    private static TestResult TestSolidBlack(ImageAnalyzer analyzer)
    {
        var sw = Stopwatch.StartNew();
        const int w = 1920, h = 1080;
        int bufSize = w * h * 4;
        IntPtr pBuf = Marshal.AllocHGlobal(bufSize);
        try
        {
            unsafe
            {
                new Span<byte>((void*)pBuf, bufSize).Clear(); // All 0 (BGR32 = 0,0,0,0)
            }

            // Test Full Range
            var histFull = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, isFullRange: true, 254, false, IntPtr.Zero, out _);
            // Test Video Range
            var histVideo = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, isFullRange: false, 235, false, IntPtr.Zero, out _);
            sw.Stop();

            bool passed = histFull.LumaBins[0] == histFull.TotalPixels
                          && histFull.ShadowCrushPercent == 100.0
                          && histFull.HighlightClipPercent == 0.0
                          && histFull.MeanLuma == 0.0
                          && histVideo.LumaBins[0] == histVideo.TotalPixels
                          && histVideo.ShadowCrushPercent == 100.0
                          && histVideo.HighlightClipPercent == 0.0
                          && histVideo.MeanLuma == 0.0;

            return new TestResult
            {
                Name = "1. Solid Black Frame (0, 0, 0)",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Sampled: {histFull.TotalPixels} px. FullRange: Bin[0]={histFull.LumaBins[0]}, ShadowCrush={histFull.ShadowCrushPercent:F1}%, Mean={histFull.MeanLuma:F1}. VideoRange: Bin[0]={histVideo.LumaBins[0]}, ShadowCrush={histVideo.ShadowCrushPercent:F1}%."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static TestResult TestSolidWhite(ImageAnalyzer analyzer)
    {
        var sw = Stopwatch.StartNew();
        const int w = 1920, h = 1080;
        int bufSize = w * h * 4;
        IntPtr pBuf = Marshal.AllocHGlobal(bufSize);
        try
        {
            unsafe
            {
                new Span<byte>((void*)pBuf, bufSize).Fill(0xFF); // All 255 (BGR32 = 255,255,255,255)
            }

            // Test Full Range
            var histFull = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, isFullRange: true, 254, false, IntPtr.Zero, out _);
            // Test Video Range
            var histVideo = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, isFullRange: false, 235, false, IntPtr.Zero, out _);
            sw.Stop();

            bool passed = histFull.LumaBins[255] == histFull.TotalPixels
                          && histFull.HighlightClipPercent == 100.0
                          && histFull.ShadowCrushPercent == 0.0
                          && Math.Abs(histFull.MeanLuma - 255.0) < 0.01
                          && histVideo.LumaBins[255] == histVideo.TotalPixels
                          && histVideo.HighlightClipPercent == 100.0
                          && histVideo.ShadowCrushPercent == 0.0
                          && Math.Abs(histVideo.MeanLuma - 255.0) < 0.01;

            return new TestResult
            {
                Name = "2. Solid White Frame (255, 255, 255)",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Sampled: {histFull.TotalPixels} px. FullRange: Bin[255]={histFull.LumaBins[255]}, HighlightClip={histFull.HighlightClipPercent:F1}%, Mean={histFull.MeanLuma:F1}. VideoRange: Bin[255]={histVideo.LumaBins[255]}, HighlightClip={histVideo.HighlightClipPercent:F1}%."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static TestResult TestNeutralGrayRamp(ImageAnalyzer analyzer)
    {
        var sw = Stopwatch.StartNew();
        // Construct image with width 256 * 4 = 1024, height 256
        // Every column x has gray value (x / 4) in [0..255]
        const int w = 1024, h = 256;
        int bufSize = w * h * 4;
        IntPtr pBuf = Marshal.AllocHGlobal(bufSize);
        try
        {
            unsafe
            {
                uint* p = (uint*)pBuf.ToPointer();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        byte gray = (byte)(x / 4);
                        p[y * w + x] = 0xFF000000 | (uint)(gray << 16) | (uint)(gray << 8) | gray;
                    }
                }
            }

            var hist = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, isFullRange: true, 254, false, IntPtr.Zero, out _);
            sw.Stop();

            // In 2x2 subsampling of w=1024, h=256:
            // x steps by 2 (512 samples per line), y steps by 2 (128 lines) = 65,536 samples
            // Every gray code 0..255 has exactly 256 samples!
            bool uniform = true;
            int expectedCount = hist.TotalPixels / 256;
            for (int i = 0; i < 256; i++)
            {
                if (hist.LumaBins[i] != expectedCount ||
                    hist.RedBins[i] != expectedCount ||
                    hist.GreenBins[i] != expectedCount ||
                    hist.BlueBins[i] != expectedCount)
                {
                    uniform = false;
                    break;
                }
            }

            double expectedMean = 255.0 / 2.0; // 127.5
            bool meanAccurate = Math.Abs(hist.MeanLuma - expectedMean) < 0.1;

            // Full range highlight clip: code 254 and 255 (2 / 256 = 0.78125%)
            double expectedClipPercent = (2.0 / 256.0) * 100.0;
            bool clipAccurate = Math.Abs(hist.HighlightClipPercent - expectedClipPercent) < 0.05;

            // Full range shadow crush: code 0 and 1 (2 / 256 = 0.78125%)
            double expectedCrushPercent = (2.0 / 256.0) * 100.0;
            bool crushAccurate = Math.Abs(hist.ShadowCrushPercent - expectedCrushPercent) < 0.05;

            bool passed = uniform && meanAccurate && clipAccurate && crushAccurate;

            return new TestResult
            {
                Name = "3. Neutral Gray Ramp (0 -> 255 Uniform Distribution)",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Sampled: {hist.TotalPixels} px. UniformBins: {uniform} ({expectedCount} per bin). Mean: {hist.MeanLuma:F2} (Exp: 127.50). Clip: {hist.HighlightClipPercent:F3}% (Exp: {expectedClipPercent:F3}%). Crush: {hist.ShadowCrushPercent:F3}% (Exp: {expectedCrushPercent:F3}%)."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static TestResult TestRgbPrimaries(ImageAnalyzer analyzer)
    {
        var sw = Stopwatch.StartNew();
        const int w = 200, h = 200;
        int bufSize = w * h * 4;
        IntPtr pBuf = Marshal.AllocHGlobal(bufSize);
        try
        {
            unsafe
            {
                uint* p = (uint*)pBuf.ToPointer();
                // 1. Pure Red (255, 0, 0): (54 * 255 + 128) >> 8 = (13770 + 128) >> 8 = 13898 >> 8 = 54
                for (int i = 0; i < w * h; i++) p[i] = 0xFFFF0000;
            }
            var hRed = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, true, 254, false, IntPtr.Zero, out _);
            int redBin54 = hRed.LumaBins[54];
            int redTotal = hRed.TotalPixels;

            unsafe
            {
                uint* p = (uint*)pBuf.ToPointer();
                // 2. Pure Green (0, 255, 0): (183 * 255 + 128) >> 8 = (46665 + 128) >> 8 = 46793 >> 8 = 182
                // Note: Weight 183 / 256 = 0.71484. 0.71484 * 255 = 182.285. Rounding yields exactly 182.
                for (int i = 0; i < w * h; i++) p[i] = 0xFF00FF00;
            }
            var hGreen = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, true, 254, false, IntPtr.Zero, out _);
            int greenBin182 = hGreen.LumaBins[182];
            int greenTotal = hGreen.TotalPixels;

            unsafe
            {
                uint* p = (uint*)pBuf.ToPointer();
                // 3. Pure Blue (0, 0, 255): (19 * 255 + 128) >> 8 = (4845 + 128) >> 8 = 4973 >> 8 = 19
                for (int i = 0; i < w * h; i++) p[i] = 0xFF0000FF;
            }
            var hBlue = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, true, 254, false, IntPtr.Zero, out _);
            int blueBin19 = hBlue.LumaBins[19];
            int blueTotal = hBlue.TotalPixels;
            sw.Stop();

            int sumOfPrimaries = 54 + 182 + 19;
            bool passed = redBin54 == redTotal
                          && greenBin182 == greenTotal
                          && blueBin19 == blueTotal
                          && sumOfPrimaries == 255;

            return new TestResult
            {
                Name = "4. RGB Primaries (Rec.709 Luma Coefficients)",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Pure Red: Y=54 ({redBin54}/{redTotal}). Pure Green: Y=182 ({greenBin182}/{greenTotal}). Pure Blue: Y=19 ({blueBin19}/{blueTotal}). Primary Sum: 54+182+19 = {sumOfPrimaries}."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static TestResult TestCodeValueBoundaries(ImageAnalyzer analyzer)
    {
        var sw = Stopwatch.StartNew();
        const int w = 100, h = 100;
        int bufSize = w * h * 4;
        IntPtr pBuf = Marshal.AllocHGlobal(bufSize);
        try
        {
            // Boundary code values to evaluate:
            // Shadows: 0, 1, 15, 16, 17
            // Highlights: 234, 235, 236, 253, 254, 255
            bool allPassed = true;
            var details = new System.Text.StringBuilder();

            int[] testValues = [0, 1, 15, 16, 17, 234, 235, 236, 253, 254, 255];
            foreach (int val in testValues)
            {
                unsafe
                {
                    uint* p = (uint*)pBuf.ToPointer();
                    uint pixel = 0xFF000000 | (uint)(val << 16) | (uint)(val << 8) | (uint)val;
                    for (int i = 0; i < w * h; i++) p[i] = pixel;
                }

                var full = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, isFullRange: true, 254, false, IntPtr.Zero, out _);
                double fullCrush = full.ShadowCrushPercent;
                double fullClip = full.HighlightClipPercent;

                var video = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, isFullRange: false, 235, false, IntPtr.Zero, out _);
                double videoCrush = video.ShadowCrushPercent;
                double videoClip = video.HighlightClipPercent;

                // Expected Full Range: Shadow <= 1, Highlight >= 254
                bool expFullCrush = val <= 1;
                bool expFullClip = val >= 254;
                bool fullOk = (fullCrush == 100.0) == expFullCrush &&
                              (fullClip == 100.0) == expFullClip;

                // Expected Video Range: Shadow <= 16, Highlight >= 235
                bool expVideoCrush = val <= 16;
                bool expVideoClip = val >= 235;
                bool videoOk = (videoCrush == 100.0) == expVideoCrush &&
                               (videoClip == 100.0) == expVideoClip;

                // Expected Broadcast Compliance metrics: SuperWhite (> 235), SubBlack (< 16)
                bool expSuperWhite = val > 235;
                bool expSubBlack = val < 16;
                bool bcastOk = (full.BroadcastSuperWhitePercent == 100.0) == expSuperWhite &&
                               (full.BroadcastSubBlackPercent == 100.0) == expSubBlack;

                // Expected Observed Min/Max Luma tracking
                bool rangeOk = full.ObservedMinLuma == val && full.ObservedMaxLuma == val;

                if (!fullOk || !videoOk || !bcastOk || !rangeOk) allPassed = false;
                details.Append($"Val {val}: Full[Crush={fullCrush:F0}%,Clip={fullClip:F0}%] Video[Crush={videoCrush:F0}%,Clip={videoClip:F0}%] Bcast[SuperW={full.BroadcastSuperWhitePercent:F0}%,SubB={full.BroadcastSubBlackPercent:F0}%]; ");
            }
            sw.Stop();

            return new TestResult
            {
                Name = "5. Exact Code-Value Boundaries (Full vs Video Range & Broadcast Compliance)",
                Passed = allPassed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = details.ToString()
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static TestResult TestCalibratedProportions(ImageAnalyzer analyzer)
    {
        var sw = Stopwatch.StartNew();
        // 4 quadrants:
        // Top-Left: Value 0 (Crushed 100% in both ranges)
        // Top-Right: Value 255 (Clipped 100% in both ranges)
        // Bottom-Left: Value 100 (Safe midtone)
        // Bottom-Right: Value 150 (Safe midtone)
        const int w = 400, h = 400;
        int bufSize = w * h * 4;
        IntPtr pBuf = Marshal.AllocHGlobal(bufSize);
        try
        {
            unsafe
            {
                uint* p = (uint*)pBuf.ToPointer();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        byte gray;
                        if (y < h / 2 && x < w / 2) gray = 0;
                        else if (y < h / 2 && x >= w / 2) gray = 255;
                        else if (y >= h / 2 && x < w / 2) gray = 100;
                        else gray = 150;

                        p[y * w + x] = 0xFF000000 | (uint)(gray << 16) | (uint)(gray << 8) | gray;
                    }
                }
            }

            var hist = analyzer.AnalyzeFrame(pBuf, w, h, w * 4, isFullRange: true, 254, false, IntPtr.Zero, out _);
            sw.Stop();

            bool clip25 = Math.Abs(hist.HighlightClipPercent - 25.0) < 0.01;
            bool crush25 = Math.Abs(hist.ShadowCrushPercent - 25.0) < 0.01;
            bool passed = clip25 && crush25;

            return new TestResult
            {
                Name = "6. Calibrated Proportions (Exact 25.0% Clip & 25.0% Crush)",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Sampled: {hist.TotalPixels} px. HighlightClip: {hist.HighlightClipPercent:F2}% (Expected: 25.00%). ShadowCrush: {hist.ShadowCrushPercent:F2}% (Expected: 25.00%)."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static TestResult TestYuy2Equivalence(ImageAnalyzer analyzer)
    {
        var sw = Stopwatch.StartNew();
        // Test both conversion pathways in ColorConverter:
        // 1. Studio-to-Full Expansion (isStudioRange: true, BT.709 integer matrix)
        // 2. Full-Range Unexpanded (isStudioRange: false)
        const int w = 512, h = 256;
        int yuy2Size = w * h * 2;
        int bgrSize = w * h * 4;

        IntPtr pYuy2 = Marshal.AllocHGlobal(yuy2Size);
        IntPtr pBgr = Marshal.AllocHGlobal(bgrSize);
        try
        {
            // Part A: BT.709 Studio [16..235] -> Full [0..255] Expansion
            unsafe
            {
                byte* pSrc = (byte*)pYuy2.ToPointer();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x += 2)
                    {
                        // Ramp from 16 to 235 across width
                        byte y0 = (byte)(16 + (x * (235 - 16)) / (w - 1));
                        byte y1 = (byte)(16 + ((x + 1) * (235 - 16)) / (w - 1));
                        int idx = (y * w + x) * 2;
                        pSrc[idx + 0] = y0;
                        pSrc[idx + 1] = 128; // U = neutral
                        pSrc[idx + 2] = y1;
                        pSrc[idx + 3] = 128; // V = neutral
                    }
                }
            }

            ColorConverter.ConvertYuy2ToBgr32(pYuy2, pBgr, w, h, isStudioRange: true);

            // Verify corner pixels in decoded BGR32
            bool studioBlackOk, studioWhiteOk;
            unsafe
            {
                byte* pDst = (byte*)pBgr.ToPointer();
                // Leftmost pixel (Y=16) must expand to 0,0,0
                studioBlackOk = (pDst[0] == 0 && pDst[1] == 0 && pDst[2] == 0);
                // Rightmost pixel (Y=235) must expand to 255,255,255
                int lastPixOffset = (w - 1) * 4;
                studioWhiteOk = (pDst[lastPixOffset] == 255 && pDst[lastPixOffset + 1] == 255 && pDst[lastPixOffset + 2] == 255);
            }

            var histExpanded = analyzer.AnalyzeFrame(pBgr, w, h, w * 4, isFullRange: true, 254, false, IntPtr.Zero, out _);

            // Part B: Full-Range Unexpanded YUY2 [0..255] -> BGR32 [0..255]
            unsafe
            {
                byte* pSrc = (byte*)pYuy2.ToPointer();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x += 2)
                    {
                        byte y0 = (byte)((x * 256) / w);
                        byte y1 = (byte)(((x + 1) * 256) / w);
                        int idx = (y * w + x) * 2;
                        pSrc[idx + 0] = y0;
                        pSrc[idx + 1] = 128;
                        pSrc[idx + 2] = y1;
                        pSrc[idx + 3] = 128;
                    }
                }
            }

            ColorConverter.ConvertYuy2ToBgr32(pYuy2, pBgr, w, h, isStudioRange: false);
            var histUnexp = analyzer.AnalyzeFrame(pBgr, w, h, w * 4, isFullRange: true, 254, false, IntPtr.Zero, out _);
            sw.Stop();

            bool passed = studioBlackOk && studioWhiteOk
                          && histExpanded.ObservedMinLuma == 0 && histExpanded.ObservedMaxLuma == 255
                          && Math.Abs(histUnexp.MeanLuma - 127.5) < 0.2;

            return new TestResult
            {
                Name = "7. YUY2 Path Mathematical Equivalence & BT.709 Studio Expansion",
                Passed = passed,
                ExecutionMs = sw.Elapsed.TotalMilliseconds,
                Details = $"Studio Expansion: Y=16->RGB=0 ({studioBlackOk}), Y=235->RGB=255 ({studioWhiteOk}), ObservedRange=[{histExpanded.ObservedMinLuma}..{histExpanded.ObservedMaxLuma}]. FullRange: MeanLuma={histUnexp.MeanLuma:F2} (Exp: 127.50)."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pYuy2);
            Marshal.FreeHGlobal(pBgr);
        }
    }

    private static TestResult TestZebraPerformanceAndCorrectness(ImageAnalyzer analyzer)
    {
        var sw = Stopwatch.StartNew();
        const int w = 1920, h = 1080;
        int bufSize = w * h * 4;
        IntPtr pSrc = Marshal.AllocHGlobal(bufSize);
        IntPtr pDst = Marshal.AllocHGlobal(bufSize);
        try
        {
            unsafe
            {
                uint* p = (uint*)pSrc.ToPointer();
                // Half clean midtone (Y=100), half clipped white (Y=255)
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        byte gray = (x < w / 2) ? (byte)100 : (byte)255;
                        p[y * w + x] = 0xFF000000 | (uint)(gray << 16) | (uint)(gray << 8) | gray;
                    }
                }
            }

            // JIT warmup
            analyzer.AnalyzeFrame(pSrc, w, h, w * 4, isFullRange: true, 235, enableZebra: true, pDst, out _);

            // Benchmark 20 consecutive frames of analysis + zebra stamping
            int iterations = 20;
            var iterSw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                analyzer.AnalyzeFrame(pSrc, w, h, w * 4, isFullRange: true, 235, enableZebra: true, pDst, out bool applied);
            }
            iterSw.Stop();
            double avgMs = iterSw.Elapsed.TotalMilliseconds / iterations;

            // Verify zebra destination buffer was modified in clipped region (x >= w/2)
            // and untouched in clean midtone region (x < w/2)
            bool unclippedClean = true;
            bool clippedStamped = false;
            unsafe
            {
                uint* pD = (uint*)pDst.ToPointer();
                for (int y = 0; y < h; y += 10)
                {
                    for (int x = 0; x < w / 2; x += 10)
                    {
                        if (pD[y * w + x] != (0xFF000000 | 100 << 16 | 100 << 8 | 100))
                        {
                            unclippedClean = false;
                            break;
                        }
                    }
                    for (int x = w / 2; x < w; x += 10)
                    {
                        uint pVal = pD[y * w + x];
                        if (pVal == 0xFFFF007F || pVal == 0xFF101010)
                        {
                            clippedStamped = true;
                        }
                    }
                }
            }
            sw.Stop();

#if DEBUG
            double latencyTarget = 33.3; // 30 FPS frame deadline in Debug builds
#else
            double latencyTarget = 16.0; // Sub-frame target in Release builds
#endif
            bool passed = unclippedClean && clippedStamped && avgMs < latencyTarget;

            return new TestResult
            {
                Name = "8. Zebra Performance & Non-Destructive Precision",
                Passed = passed,
                ExecutionMs = avgMs,
                Details = $"Avg latency at 1080p: {avgMs:F2} ms (Target: <{latencyTarget:F1} ms). Unclipped clean: {unclippedClean}. Clipped stamped: {clippedStamped}."
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pSrc);
            Marshal.FreeHGlobal(pDst);
        }
    }
}
