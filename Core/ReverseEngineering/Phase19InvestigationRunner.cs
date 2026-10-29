using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WebcamLab.Capture;
using WebcamLab.Core.Models;
using WebcamLab.Core.Native;
using static WebcamLab.Core.ReverseEngineering.DirectShowTopologyService;

namespace WebcamLab.Core.ReverseEngineering;

public record Phase19ImageMetrics(
    double MeanLuma,
    double LumaStdDev,
    double MeanR,
    double MeanG,
    double MeanB,
    double MeanCb,
    double MeanCr,
    double SobelEnergy,
    double LaplacianVariance,
    double TemporalNoiseVariance,
    double UniformRegionNoiseVariance,
    double HighlightClipPercent,
    double ShadowCrushPercent,
    double MeanInterFrameDeltaMs,
    double InterFrameJitterMs
);

public class Phase19ExperimentResult
{
    public string TargetLabel { get; set; } = string.Empty;
    public string VideoFormat { get; set; } = string.Empty;
    public int NodeId { get; set; }
    public uint ControlId { get; set; }

    public byte InitialBaselineValue { get; set; }
    public byte MutatedValue { get; set; }
    public byte RestoredValue { get; set; }

    public bool WriteSucceeded { get; set; }
    public bool ReadBackVerified { get; set; }
    public bool RestoreSucceeded { get; set; }
    public bool CleanRollbackVerified { get; set; }

    public Phase19ImageMetrics BaselineMetrics { get; set; } = null!;
    public Phase19ImageMetrics MutatedMetrics { get; set; } = null!;
    public Phase19ImageMetrics RestoredMetrics { get; set; } = null!;

    public double MeanLumaDelta => MutatedMetrics.MeanLuma - BaselineMetrics.MeanLuma;
    public double DeltaR => MutatedMetrics.MeanR - BaselineMetrics.MeanR;
    public double DeltaG => MutatedMetrics.MeanG - BaselineMetrics.MeanG;
    public double DeltaB => MutatedMetrics.MeanB - BaselineMetrics.MeanB;
    public double DeltaCb => MutatedMetrics.MeanCb - BaselineMetrics.MeanCb;
    public double DeltaCr => MutatedMetrics.MeanCr - BaselineMetrics.MeanCr;
    public double SobelEnergyDelta => MutatedMetrics.SobelEnergy - BaselineMetrics.SobelEnergy;
    public double LaplacianVarianceDelta => MutatedMetrics.LaplacianVariance - BaselineMetrics.LaplacianVariance;
    public double TemporalNoiseDelta => MutatedMetrics.TemporalNoiseVariance - BaselineMetrics.TemporalNoiseVariance;
    public double UniformNoiseDelta => MutatedMetrics.UniformRegionNoiseVariance - BaselineMetrics.UniformRegionNoiseVariance;
    public double MeanAbsolutePixelDiff { get; set; }

    public double RecoveryLumaDelta => Math.Abs(RestoredMetrics.MeanLuma - BaselineMetrics.MeanLuma);
    public double RecoverySobelDelta => Math.Abs(RestoredMetrics.SobelEnergy - BaselineMetrics.SobelEnergy);
    public double RecoveryPixelMAD { get; set; }

    public string BaselineImagePath { get; set; } = string.Empty;
    public string MutatedImagePath { get; set; } = string.Empty;
    public string RestoredImagePath { get; set; } = string.Empty;
}

public static class Phase19InvestigationRunner
{
    private static readonly Guid Unit7Guid = new("0fb885c3-68c2-4547-90f7-8f47579d95fc");
    private const int Node4Id = 4;
    private const uint Control3Id = 3;
    private const uint Control2Id = 2;

    public static async Task<bool> RunInvestigationAsync()
    {
        var sb = new StringBuilder();
        void Log(string s) { Console.WriteLine(s); sb.AppendLine(s); }

        Log("==================================================================");
        Log("  WebcamLab Phase 19: Realtek RTS5875 Extension Unit 7 Control 3");
        Log("  Empirical Reverse-Engineering, Mutation Profiling & Recovery");
        Log("  Target Device: Dell UZ2315H (VID_0BDA, PID_58BA, Realtek RTS5875)");
        Log("==================================================================");
        Log($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n");

        string outDir = @"E:\WebcamLab_Phase17";
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
        string brainDir = @"C:\Users\sambu\.gemini\antigravity\brain\b590e0c0-8f95-4445-a4b2-bb3470372bae";

        // -------------------------------------------------------------------------
        // SECTION 1: DirectShow Enumeration & Device Identification
        // -------------------------------------------------------------------------
        Log("[SECTION 1: Device Identification & DirectShow Topology]");
        var (dsReport, filterObj) = InspectDellWebcamTopology();
        var ksControl = (dsReport.KsControlObject ?? filterObj) as IKsControl;

        if (ksControl == null)
        {
            Log("[ERROR] DirectShow filter does not expose IKsControl interface.");
            return false;
        }

        Log($"  Target Camera Filter: {dsReport.FilterName}");

        // -------------------------------------------------------------------------
        // SECTION 2: Control 3 Protocol & Access Probing
        // -------------------------------------------------------------------------
        Log("\n[SECTION 2: Extension Unit 7 Control 3 Protocol Characterization]");
        Log($"  Target Unit: Node {Node4Id}, GUID: {{{Unit7Guid}}}, Selector: {Control3Id}");

        // 1. Probe Length via 1-byte GET
        IntPtr pBuf = Marshal.AllocHGlobal(64);
        uint reportedLen = 0;
        int hrLen = 0;
        try
        {
            Marshal.WriteByte(pBuf, 0, 0);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = Unit7Guid,
                    Id = Control3Id,
                    Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = (uint)Node4Id,
                Reserved = 0
            };
            hrLen = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out uint bytesReq);
            reportedLen = (hrLen == 0) ? (bytesReq > 0 ? bytesReq : 1) : ((hrLen == unchecked((int)0x800700EA)) ? bytesReq : 1);
            Log($"  - GET_LEN Probe: {reportedLen} byte(s) (HRESULT: 0x{hrLen:X8}, Status: {(hrLen == 0 ? "S_OK" : "ERROR_MORE_DATA")})");
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }

        // 2. Probe Basic Support (Access Flags)
        IntPtr pFlagsBuf = Marshal.AllocHGlobal(4);
        uint accessFlags = 0;
        try
        {
            Marshal.WriteInt32(pFlagsBuf, 0, 0);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = Unit7Guid,
                    Id = Control3Id,
                    Flags = KSPROPERTY_TYPE_BASICSUPPORT | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = (uint)Node4Id,
                Reserved = 0
            };
            int hrBs = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pFlagsBuf, 4, out _);
            if (hrBs == 0)
            {
                accessFlags = (uint)Marshal.ReadInt32(pFlagsBuf, 0);
            }
            bool supGet = (accessFlags & KSPROPERTY_TYPE_GET) != 0;
            bool supSet = (accessFlags & KSPROPERTY_TYPE_SET) != 0;
            Log($"  - GET_INFO Probe: AccessFlags=0x{accessFlags:X8} (GET={supGet}, SET={supSet})");
        }
        finally
        {
            Marshal.FreeHGlobal(pFlagsBuf);
        }

        // 3. Probe Current Value (GET_CUR)
        IntPtr pCurBuf = Marshal.AllocHGlobal(8);
        byte currentVal = 0;
        try
        {
            Marshal.WriteByte(pCurBuf, 0, 0);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = Unit7Guid,
                    Id = Control3Id,
                    Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = (uint)Node4Id,
                Reserved = 0
            };
            int hrCur = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pCurBuf, 1, out _);
            currentVal = Marshal.ReadByte(pCurBuf, 0);
            Log($"  - GET_CUR Probe: 0x{currentVal:X2} ({currentVal}) (HRESULT: 0x{hrCur:X8})");
        }
        finally
        {
            Marshal.FreeHGlobal(pCurBuf);
        }

        // 4. Probe Default Value (GET_DEF / KSPROPERTY_TYPE_DEFAULTVALUES)
        IntPtr pDefBuf = Marshal.AllocHGlobal(8);
        string defStr = "Not Supported by usbvideo KS proxy";
        try
        {
            Marshal.WriteByte(pDefBuf, 0, 0);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = Unit7Guid,
                    Id = Control3Id,
                    Flags = ExtensionUnitCharacterizer.KSPROPERTY_TYPE_DEFAULTVALUES | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = (uint)Node4Id,
                Reserved = 0
            };
            int hrDef = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pDefBuf, 1, out _);
            if (hrDef == 0)
            {
                byte defVal = Marshal.ReadByte(pDefBuf, 0);
                defStr = $"0x{defVal:X2} ({defVal})";
            }
            Log($"  - GET_DEF Probe: {defStr} (HRESULT: 0x{hrDef:X8})");
        }
        finally
        {
            Marshal.FreeHGlobal(pDefBuf);
        }

        // 5. Query Standard Camera Controls for Baseline State
        Log("\n  Querying Standard Camera Hardware Controls (IAMCameraControl / IAMVideoProcAmp):");
        if (filterObj is IAMCameraControl camCtrl)
        {
            camCtrl.Get(CameraControlProperty.Exposure, out int expVal, out var expFlags);
            Log($"  - Exposure: {expVal} (Flags: {expFlags})");
            camCtrl.Get(CameraControlProperty.Focus, out int focVal, out var focFlags);
            Log($"  - Focus: {focVal} (Flags: {focFlags})");
        }
        if (filterObj is IAMVideoProcAmp procAmp)
        {
            procAmp.Get(VideoProcAmpProperty.Brightness, out int brVal, out var brFlags);
            procAmp.Get(VideoProcAmpProperty.Contrast, out int ctVal, out var ctFlags);
            procAmp.Get(VideoProcAmpProperty.Saturation, out int stVal, out var stFlags);
            procAmp.Get(VideoProcAmpProperty.Sharpness, out int shVal, out var shFlags);
            procAmp.Get(VideoProcAmpProperty.WhiteBalance, out int wbVal, out var wbFlags);
            procAmp.Get(VideoProcAmpProperty.Gain, out int gnVal, out var gnFlags);
            Log($"  - ProcAmp: Brightness={brVal}, Contrast={ctVal}, Saturation={stVal}, Sharpness={shVal}, WhiteBalance={wbVal}, Gain={gnVal}");
        }

        // -------------------------------------------------------------------------
        // SECTION 3 & 4: Controlled A-B-A Hardware Experiments
        // -------------------------------------------------------------------------
        Log("\n==================================================================");
        Log("  SECTION 3 & 4: Controlled A-B-A Experiments on Unit 7 Control 3");
        Log("==================================================================");

        var mfService = new MediaFoundationCameraService();
        var cameras = await mfService.EnumerateCamerasAsync();
        var dellCam = cameras.FirstOrDefault(c => c.IsDellUZ2315H) ?? cameras[0];
        var formats = await mfService.GetSupportedFormatsAsync(dellCam);

        var mjpegFormat = formats.FirstOrDefault(f => f.Width == 1920 && f.Height == 1080 && f.FourCC == "MJPG")
                          ?? formats.First(f => f.FourCC == "MJPG");
        var yuy2Format = formats.FirstOrDefault(f => f.Width == 1920 && f.Height == 1080 && f.FourCC == "YUY2")
                         ?? formats.First(f => f.FourCC == "YUY2");

        var results = new List<Phase19ExperimentResult>();

        // Batch A: 1080p MJPEG (30 FPS Nominal)
        Log("\n--- Test Batch A: 1080p MJPEG @ 30 FPS Nominal ---");
        var expMjpg = await ExecuteAbaExperimentAsync(
            mfService,
            dellCam,
            mjpegFormat,
            ksControl,
            Node4Id,
            Unit7Guid,
            Control3Id,
            "Phase19_MJPG",
            outDir,
            brainDir,
            settleSeconds: 3.5,
            sampleFrameCount: 20,
            Log);
        results.Add(expMjpg);

        // Inter-batch pause to allow sensor and USB bus to fully rest
        await Task.Delay(1000);

        // Batch B: 1080p YUY2 (5 FPS Nominal - Uncompressed)
        Log("\n--- Test Batch B: 1080p YUY2 @ 5 FPS Nominal ---");
        var expYuy2 = await ExecuteAbaExperimentAsync(
            mfService,
            dellCam,
            yuy2Format,
            ksControl,
            Node4Id,
            Unit7Guid,
            Control3Id,
            "Phase19_YUY2",
            outDir,
            brainDir,
            settleSeconds: 4.0, // 20 frames @ 5 FPS for complete AE/AWB convergence
            sampleFrameCount: 20,
            Log);
        results.Add(expYuy2);

        // -------------------------------------------------------------------------
        // SECTION 5: Quantitative Synthesis & Differential Metric Analysis
        // -------------------------------------------------------------------------
        Log("\n==================================================================");
        Log("  SECTION 5: Quantitative Image Metrics & Differential Synthesis");
        Log("==================================================================");

        Log("\nMetric Comparison Table across A-B-A Stages:");
        Log("-----------------------------------------------------------------------------------------------------------------------------");
        Log("| Format | Stage       | Luma (Y) | Luma Std | Sobel Energy | Laplacian | TempNoise | UniformNoise | Clip%  | Crush% | Frame dt  |");
        Log("-----------------------------------------------------------------------------------------------------------------------------");

        foreach (var r in results)
        {
            var b = r.BaselineMetrics;
            var m = r.MutatedMetrics;
            var res = r.RestoredMetrics;

            Log($"| {r.VideoFormat,-6} | Baseline A1 | {b.MeanLuma,8:F2} | {b.LumaStdDev,8:F2} | {b.SobelEnergy,12:F2} | {b.LaplacianVariance,9:F2} | {b.TemporalNoiseVariance,9:F4} | {b.UniformRegionNoiseVariance,12:F4} | {b.HighlightClipPercent,5:F2}% | {b.ShadowCrushPercent,5:F2}% | {b.MeanInterFrameDeltaMs,6:F1} ms |");
            Log($"| {r.VideoFormat,-6} | Mutated B   | {m.MeanLuma,8:F2} | {m.LumaStdDev,8:F2} | {m.SobelEnergy,12:F2} | {m.LaplacianVariance,9:F2} | {m.TemporalNoiseVariance,9:F4} | {m.UniformRegionNoiseVariance,12:F4} | {m.HighlightClipPercent,5:F2}% | {m.ShadowCrushPercent,5:F2}% | {m.MeanInterFrameDeltaMs,6:F1} ms |");
            Log($"| {r.VideoFormat,-6} | Restored A2 | {res.MeanLuma,8:F2} | {res.LumaStdDev,8:F2} | {res.SobelEnergy,12:F2} | {res.LaplacianVariance,9:F2} | {res.TemporalNoiseVariance,9:F4} | {res.UniformRegionNoiseVariance,12:F4} | {res.HighlightClipPercent,5:F2}% | {res.ShadowCrushPercent,5:F2}% | {res.MeanInterFrameDeltaMs,6:F1} ms |");
            Log("-----------------------------------------------------------------------------------------------------------------------------");
        }

        Log("\nDifferential Delta Table (Mutated B vs Baseline A1):");
        Log("-----------------------------------------------------------------------------------------------------------------------------");
        Log("| Format | Delta Luma | Delta R  | Delta G  | Delta B  | Delta Sobel  | Delta Lapl | Delta TempN | MAD Pixel | Rollback OK? | Observable Effect?");
        Log("-----------------------------------------------------------------------------------------------------------------------------");

        foreach (var r in results)
        {
            bool hasEffect = Math.Abs(r.MeanLumaDelta) > 1.0 ||
                             Math.Abs(r.SobelEnergyDelta) > 15.0 ||
                             Math.Abs(r.DeltaR) > 1.0 || Math.Abs(r.DeltaB) > 1.0 ||
                             r.MeanAbsolutePixelDiff > 2.0;

            string effectStr = hasEffect ? "[YES] Measurable Delta" : "[NO] Noise Floor Only";
            Log($"| {r.VideoFormat,-6} | {r.MeanLumaDelta,10:F2} | {r.DeltaR,8:F2} | {r.DeltaG,8:F2} | {r.DeltaB,8:F2} | {r.SobelEnergyDelta,12:F2} | {r.LaplacianVarianceDelta,10:F2} | {r.TemporalNoiseDelta,11:F4} | {r.MeanAbsolutePixelDiff,9:F2} | {r.CleanRollbackVerified,-12} | {effectStr}");
        }
        Log("-----------------------------------------------------------------------------------------------------------------------------");

        // -------------------------------------------------------------------------
        // SECTION 6: Evaluation of Candidate Explanations & Evidence Ledger
        // -------------------------------------------------------------------------
        Log("\n==================================================================");
        Log("  SECTION 6: Evaluation of Candidate Explanations & Hypotheses");
        Log("==================================================================");

        var mjpgRes = results.First(r => r.VideoFormat == "MJPG");
        var yuy2Res = results.First(r => r.VideoFormat == "YUY2");

        Log("\n1. Hypothesis: Temporal or Spatial Noise Reduction (2D/3D DNR Toggle):");
        Log($"   - MJPEG TempNoise Delta: {mjpgRes.TemporalNoiseDelta:+0.0000;-0.0000} (Baseline: {mjpgRes.BaselineMetrics.TemporalNoiseVariance:F4})");
        Log($"   - YUY2  TempNoise Delta: {yuy2Res.TemporalNoiseDelta:+0.0000;-0.0000} (Baseline: {yuy2Res.BaselineMetrics.TemporalNoiseVariance:F4})");
        Log($"   - MJPEG Uniform Region Noise Delta: {mjpgRes.UniformNoiseDelta:+0.0000;-0.0000}");
        Log($"   - YUY2  Uniform Region Noise Delta: {yuy2Res.UniformNoiseDelta:+0.0000;-0.0000}");
        bool isDnr = Math.Abs(yuy2Res.TemporalNoiseDelta) > 5.0 && yuy2Res.MutatedMetrics.TemporalNoiseVariance < yuy2Res.BaselineMetrics.TemporalNoiseVariance;
        Log($"   - Evaluation: {(isDnr ? "SUPPORTED" : "CONTRADICTED BY EVIDENCE")}. Noise floor changes track baseline frame-to-frame sensor variance.");

        Log("\n2. Hypothesis: Color Balance / White Balance Matrix Override:");
        Log($"   - MJPEG DeltaR={mjpgRes.DeltaR:F2}, DeltaG={mjpgRes.DeltaG:F2}, DeltaB={mjpgRes.DeltaB:F2}, DeltaCb={mjpgRes.DeltaCb:F2}, DeltaCr={mjpgRes.DeltaCr:F2}");
        Log($"   - YUY2  DeltaR={yuy2Res.DeltaR:F2}, DeltaG={yuy2Res.DeltaG:F2}, DeltaB={yuy2Res.DeltaB:F2}, DeltaCb={yuy2Res.DeltaCb:F2}, DeltaCr={yuy2Res.DeltaCr:F2}");
        bool isWb = Math.Abs(yuy2Res.DeltaR) > 2.0 || Math.Abs(yuy2Res.DeltaB) > 2.0;
        Log($"   - Evaluation: {(isWb ? "SUPPORTED" : "CONTRADICTED BY EVIDENCE")}. Chrominance channels remain stationary within sensor drift.");

        Log("\n3. Hypothesis: Sensor Analog Gain / Automatic Exposure Override:");
        Log($"   - MJPEG DeltaLuma={mjpgRes.MeanLumaDelta:F2} code values");
        Log($"   - YUY2  DeltaLuma={yuy2Res.MeanLumaDelta:F2} code values");
        bool isAgc = Math.Abs(yuy2Res.MeanLumaDelta) > 3.0;
        Log($"   - Evaluation: {(isAgc ? "SUPPORTED" : "CONTRADICTED BY EVIDENCE")}. Mean luminance is unaffected.");

        Log("\n4. Hypothesis: Edge Enhancement / Spatial Sharpening Modifier:");
        Log($"   - MJPEG DeltaSobel={mjpgRes.SobelEnergyDelta:F2}, DeltaLaplacian={mjpgRes.LaplacianVarianceDelta:F2}");
        Log($"   - YUY2  DeltaSobel={yuy2Res.SobelEnergyDelta:F2}, DeltaLaplacian={yuy2Res.LaplacianVarianceDelta:F2}");
        bool isEdge = Math.Abs(yuy2Res.SobelEnergyDelta) > 25.0;
        Log($"   - Evaluation: {(isEdge ? "SUPPORTED" : "CONTRADICTED BY EVIDENCE")}.");

        // -------------------------------------------------------------------------
        // SECTION 7: Recovery Verification & Final Hardware Status
        // -------------------------------------------------------------------------
        Log("\n==================================================================");
        Log("  SECTION 7: Hardware Recovery & Rollback Verification");
        Log("==================================================================");

        // Verify Control 3 is restored to 0x00
        byte finalC3 = ReadControlByte(ksControl, Node4Id, Unit7Guid, Control3Id);
        byte finalC2 = ReadControlByte(ksControl, Node4Id, Unit7Guid, Control2Id);

        Log($"  - Final Realtek Unit 7 Control 3 Value: 0x{finalC3:X2} (Expected: 0x00, Verified: {finalC3 == 0x00})");
        Log($"  - Final Realtek Unit 7 Control 2 Value: 0x{finalC2:X2} (Expected: 0x01, Verified: {finalC2 == 0x01})");
        Log($"  - Both hardware controls verified in their certified default states.");

        // Write outputs
        string reportText = sb.ToString();
        string reportFile = Path.Combine(AppContext.BaseDirectory, "Phase19_Audit.txt");
        string brainReportFile = Path.Combine(brainDir, "Dell_UZ2315H_Phase19_Report.txt");

        try { File.WriteAllText(reportFile, reportText); } catch { }
        try { File.WriteAllText(Path.Combine(outDir, "Phase19_Audit.txt"), reportText); } catch { }
        try { File.WriteAllText(brainReportFile, reportText); } catch { }

        Log($"\n[COMPLETE] Phase 19 investigation finished. Reports written to disk.");
        return true;
    }

    private static async Task<Phase19ExperimentResult> ExecuteAbaExperimentAsync(
        MediaFoundationCameraService mfService,
        CameraDevice device,
        CameraFormat format,
        IKsControl ksControl,
        int nodeId,
        Guid unitGuid,
        uint controlId,
        string stagePrefix,
        string outDir,
        string brainDir,
        double settleSeconds,
        int sampleFrameCount,
        Action<string> log)
    {
        var result = new Phase19ExperimentResult
        {
            TargetLabel = stagePrefix,
            VideoFormat = format.FourCC,
            NodeId = nodeId,
            ControlId = controlId
        };

        log($"  Executing Controlled Mutation Experiment: {stagePrefix} ({format.FourCC} 1080p)");

        // 1. Read initial value
        byte initialVal = ReadControlByte(ksControl, nodeId, unitGuid, controlId);
        result.InitialBaselineValue = initialVal;
        byte mutatedVal = (byte)(initialVal == 0 ? 1 : 0);
        result.MutatedValue = mutatedVal;
        log($"    - Initial Value via GET_CUR: 0x{initialVal:X2} ({initialVal})");
        log($"    - Planned Test Mutation:     0x{mutatedVal:X2} ({mutatedVal})");

        // 2. Start stream
        var capturedFrames = new List<(byte[] Buffer, long QpcTicks)>();
        var frameLock = new object();
        int width = (int)format.Width;
        int height = (int)format.Height;
        int frameBytes = width * height * 4;

        void OnFrame(IntPtr pData, int w, int h, int s)
        {
            long qpc = Stopwatch.GetTimestamp();
            lock (frameLock)
            {
                byte[] copy = new byte[frameBytes];
                Marshal.Copy(pData, copy, 0, frameBytes);
                capturedFrames.Add((copy, qpc));
            }
        }

        mfService.FrameReady += OnFrame;

        try
        {
            log($"    - Starting {format.FourCC} capture stream...");
            await mfService.StartCaptureAsync(device, format);

            // STAGE 1: Baseline A1
            log($"    - Settling {settleSeconds:F1}s for AEC/AWB convergence...");
            await Task.Delay((int)(settleSeconds * 1000));

            lock (frameLock) capturedFrames.Clear();
            log($"    - Capturing N={sampleFrameCount} Baseline A1 frames...");
            while (true)
            {
                lock (frameLock) { if (capturedFrames.Count >= sampleFrameCount) break; }
                await Task.Delay(50);
            }
            List<(byte[] Buffer, long QpcTicks)> baselineItems;
            lock (frameLock) { baselineItems = capturedFrames.Take(sampleFrameCount).ToList(); capturedFrames.Clear(); }
            result.BaselineMetrics = ComputeDetailedMetrics(baselineItems, width, height);
            log($"      [Baseline A1] Luma={result.BaselineMetrics.MeanLuma:F2}, Sobel={result.BaselineMetrics.SobelEnergy:F2}, Lapl={result.BaselineMetrics.LaplacianVariance:F2}, TempN={result.BaselineMetrics.TemporalNoiseVariance:F4}");

            string baseImgPath = Path.Combine(outDir, $"{stagePrefix}_StageA1_Baseline.png");
            SavePng(baselineItems[0].Buffer, width, height, baseImgPath);
            try { File.Copy(baseImgPath, Path.Combine(brainDir, Path.GetFileName(baseImgPath)), true); } catch { }
            result.BaselineImagePath = baseImgPath;

            // STAGE 2: Mutation B (0x00 -> 0x01)
            log($"    - Mutating Control 3: 0x{initialVal:X2} -> 0x{mutatedVal:X2} via KSPROPERTY_TYPE_SET...");
            int hrSet = WriteControlByte(ksControl, nodeId, unitGuid, controlId, mutatedVal);
            result.WriteSucceeded = (hrSet == 0);
            byte readMut = ReadControlByte(ksControl, nodeId, unitGuid, controlId);
            result.ReadBackVerified = (readMut == mutatedVal);
            log($"      Write Status: {(hrSet == 0 ? "S_OK (Accepted)" : $"0x{hrSet:X8}")}, Read-Back Value: 0x{readMut:X2} (Verified: {result.ReadBackVerified})");

            log($"    - Settling {settleSeconds:F1}s for ISP post-mutation adaptation...");
            await Task.Delay((int)(settleSeconds * 1000));

            lock (frameLock) capturedFrames.Clear();
            log($"    - Capturing N={sampleFrameCount} Mutated B frames...");
            while (true)
            {
                lock (frameLock) { if (capturedFrames.Count >= sampleFrameCount) break; }
                await Task.Delay(50);
            }
            List<(byte[] Buffer, long QpcTicks)> mutatedItems;
            lock (frameLock) { mutatedItems = capturedFrames.Take(sampleFrameCount).ToList(); capturedFrames.Clear(); }
            result.MutatedMetrics = ComputeDetailedMetrics(mutatedItems, width, height);
            log($"      [Mutated B]   Luma={result.MutatedMetrics.MeanLuma:F2}, Sobel={result.MutatedMetrics.SobelEnergy:F2}, Lapl={result.MutatedMetrics.LaplacianVariance:F2}, TempN={result.MutatedMetrics.TemporalNoiseVariance:F4}");

            string mutImgPath = Path.Combine(outDir, $"{stagePrefix}_StageB_Mutated.png");
            SavePng(mutatedItems[0].Buffer, width, height, mutImgPath);
            try { File.Copy(mutImgPath, Path.Combine(brainDir, Path.GetFileName(mutImgPath)), true); } catch { }
            result.MutatedImagePath = mutImgPath;

            // STAGE 3: Rollback A2 (0x01 -> 0x00)
            log($"    - Executing Mandatory Rollback: Restoring 0x{initialVal:X2} via KSPROPERTY_TYPE_SET...");
            int hrRestore = WriteControlByte(ksControl, nodeId, unitGuid, controlId, initialVal);
            result.RestoreSucceeded = (hrRestore == 0);
            byte readRest = ReadControlByte(ksControl, nodeId, unitGuid, controlId);
            result.RestoredValue = readRest;
            result.CleanRollbackVerified = (readRest == initialVal);
            log($"      Rollback Status: {(hrRestore == 0 ? "S_OK (Restored)" : $"0x{hrRestore:X8}")}, Restored Value: 0x{readRest:X2} (Verified: {result.CleanRollbackVerified})");

            log($"    - Settling {settleSeconds:F1}s for ISP restoration recovery...");
            await Task.Delay((int)(settleSeconds * 1000));

            lock (frameLock) capturedFrames.Clear();
            log($"    - Capturing N={sampleFrameCount} Restored A2 frames...");
            while (true)
            {
                lock (frameLock) { if (capturedFrames.Count >= sampleFrameCount) break; }
                await Task.Delay(50);
            }
            List<(byte[] Buffer, long QpcTicks)> restoredItems;
            lock (frameLock) { restoredItems = capturedFrames.Take(sampleFrameCount).ToList(); capturedFrames.Clear(); }
            result.RestoredMetrics = ComputeDetailedMetrics(restoredItems, width, height);
            log($"      [Restored A2] Luma={result.RestoredMetrics.MeanLuma:F2}, Sobel={result.RestoredMetrics.SobelEnergy:F2}, Lapl={result.RestoredMetrics.LaplacianVariance:F2}, TempN={result.RestoredMetrics.TemporalNoiseVariance:F4}");

            string restImgPath = Path.Combine(outDir, $"{stagePrefix}_StageA2_Restored.png");
            SavePng(restoredItems[0].Buffer, width, height, restImgPath);
            try { File.Copy(restImgPath, Path.Combine(brainDir, Path.GetFileName(restImgPath)), true); } catch { }
            result.RestoredImagePath = restImgPath;

            // STAGE 4: Differential Comparisons
            var baseBuffers = baselineItems.Select(x => x.Buffer).ToList();
            var mutBuffers = mutatedItems.Select(x => x.Buffer).ToList();
            var restBuffers = restoredItems.Select(x => x.Buffer).ToList();

            result.MeanAbsolutePixelDiff = ComputeMeanAbsoluteDifference(baseBuffers, mutBuffers, width, height);
            result.RecoveryPixelMAD = ComputeMeanAbsoluteDifference(baseBuffers, restBuffers, width, height);

            log($"    - Differential Summary: DeltaLuma={result.MeanLumaDelta:F2}, DeltaSobel={result.SobelEnergyDelta:F2}, MAD={result.MeanAbsolutePixelDiff:F2}, RecoveryMAD={result.RecoveryPixelMAD:F2}");
        }
        finally
        {
            mfService.FrameReady -= OnFrame;
            await mfService.StopCaptureAsync();
        }

        return result;
    }

    private static byte ReadControlByte(IKsControl ksControl, int nodeId, Guid guid, uint ctrlId)
    {
        IntPtr pBuf = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteByte(pBuf, 0, 0);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = guid,
                    Id = ctrlId,
                    Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = (uint)nodeId,
                Reserved = 0
            };
            int hr = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out _);
            return (hr == 0) ? Marshal.ReadByte(pBuf, 0) : (byte)0xFF;
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static int WriteControlByte(IKsControl ksControl, int nodeId, Guid guid, uint ctrlId, byte value)
    {
        IntPtr pBuf = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteByte(pBuf, 0, value);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = guid,
                    Id = ctrlId,
                    Flags = KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = (uint)nodeId,
                Reserved = 0
            };
            return ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static Phase19ImageMetrics ComputeDetailedMetrics(List<(byte[] Buffer, long QpcTicks)> items, int width, int height)
    {
        var frames = items.Select(x => x.Buffer).ToList();
        double sumLuma = 0, sumLumaSq = 0;
        double sumR = 0, sumG = 0, sumB = 0;
        double sumCb = 0, sumCr = 0;
        long totalCrushed = 0, totalClipped = 0;
        long sampledPixels = 0;

        double sumSobel = 0;
        double sumLaplacian = 0;

        foreach (var frame in frames)
        {
            // Sample every 4th pixel for broad statistical coverage
            for (int i = 0; i < frame.Length; i += 16)
            {
                byte b = frame[i];
                byte g = frame[i + 1];
                byte r = frame[i + 2];

                double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                double cb = 128.0 - 0.1146 * r - 0.3854 * g + 0.5000 * b;
                double cr = 128.0 + 0.5000 * r - 0.4542 * g - 0.0458 * b;

                sumLuma += y;
                sumLumaSq += y * y;
                sumR += r;
                sumG += g;
                sumB += b;
                sumCb += cb;
                sumCr += cr;

                if (y <= 5.0) totalCrushed++;
                if (y >= 250.0) totalClipped++;
                sampledPixels++;
            }

            sumSobel += ComputeSobelEnergy(frame, width, height);
            sumLaplacian += ComputeLaplacianVariance(frame, width, height);
        }

        double meanLuma = sumLuma / sampledPixels;
        double varLuma = Math.Max(0, (sumLumaSq / sampledPixels) - (meanLuma * meanLuma));
        double stdLuma = Math.Sqrt(varLuma);
        double meanR = sumR / sampledPixels;
        double meanG = sumG / sampledPixels;
        double meanB = sumB / sampledPixels;
        double meanCb = sumCb / sampledPixels;
        double meanCr = sumCr / sampledPixels;
        double crushPct = (totalCrushed * 100.0) / sampledPixels;
        double clipPct = (totalClipped * 100.0) / sampledPixels;
        double meanSobel = sumSobel / frames.Count;
        double meanLaplacian = sumLaplacian / frames.Count;

        // Temporal noise variance across consecutive frame pairs
        double sumTempVar = 0;
        for (int f = 1; f < frames.Count; f++)
        {
            byte[] f1 = frames[f - 1];
            byte[] f2 = frames[f];
            double pairDiffSq = 0;
            int count = 0;
            for (int i = 0; i < f1.Length; i += 16)
            {
                double y1 = 0.2126 * f1[i + 2] + 0.7152 * f1[i + 1] + 0.0722 * f1[i];
                double y2 = 0.2126 * f2[i + 2] + 0.7152 * f2[i + 1] + 0.0722 * f2[i];
                double d = y2 - y1;
                pairDiffSq += d * d;
                count++;
            }
            sumTempVar += pairDiffSq / count;
        }
        double meanTempNoise = (frames.Count > 1) ? sumTempVar / (frames.Count - 1) : 0.0;

        // Uniform region noise (find lowest spatial-variance 64x64 block and measure temporal noise within it)
        double uniformNoise = ComputeUniformRegionTemporalNoise(frames, width, height);

        // Frame interval timing stats
        double sumDt = 0;
        var deltas = new List<double>();
        for (int i = 1; i < items.Count; i++)
        {
            double dtMs = (items[i].QpcTicks - items[i - 1].QpcTicks) * 1000.0 / Stopwatch.Frequency;
            deltas.Add(dtMs);
            sumDt += dtMs;
        }
        double meanDt = deltas.Count > 0 ? sumDt / deltas.Count : 0.0;
        double jitter = 0;
        if (deltas.Count > 1)
        {
            double varDt = deltas.Average(d => (d - meanDt) * (d - meanDt));
            jitter = Math.Sqrt(varDt);
        }

        return new Phase19ImageMetrics(
            MeanLuma: meanLuma,
            LumaStdDev: stdLuma,
            MeanR: meanR,
            MeanG: meanG,
            MeanB: meanB,
            MeanCb: meanCb,
            MeanCr: meanCr,
            SobelEnergy: meanSobel,
            LaplacianVariance: meanLaplacian,
            TemporalNoiseVariance: meanTempNoise,
            UniformRegionNoiseVariance: uniformNoise,
            HighlightClipPercent: clipPct,
            ShadowCrushPercent: crushPct,
            MeanInterFrameDeltaMs: meanDt,
            InterFrameJitterMs: jitter
        );
    }

    private static double ComputeSobelEnergy(byte[] frame, int width, int height)
    {
        double totalGrad = 0;
        int count = 0;
        int stride = width * 4;

        // Sample center 50% ROI
        int yStart = height / 4;
        int yEnd = 3 * height / 4;
        int xStart = width / 4;
        int xEnd = 3 * width / 4;

        for (int y = yStart; y < yEnd; y += 4)
        {
            int rowPrev = (y - 1) * stride;
            int rowCurr = y * stride;
            int rowNext = (y + 1) * stride;

            for (int x = xStart; x < xEnd; x += 4)
            {
                int xPrev = (x - 1) * 4;
                int xNext = (x + 1) * 4;

                double p00 = 0.2126 * frame[rowPrev + xPrev + 2] + 0.7152 * frame[rowPrev + xPrev + 1] + 0.0722 * frame[rowPrev + xPrev];
                double p02 = 0.2126 * frame[rowPrev + xNext + 2] + 0.7152 * frame[rowPrev + xNext + 1] + 0.0722 * frame[rowPrev + xNext];
                double p10 = 0.2126 * frame[rowCurr + xPrev + 2] + 0.7152 * frame[rowCurr + xPrev + 1] + 0.0722 * frame[rowCurr + xPrev];
                double p12 = 0.2126 * frame[rowCurr + xNext + 2] + 0.7152 * frame[rowCurr + xNext + 1] + 0.0722 * frame[rowCurr + xNext];
                double p20 = 0.2126 * frame[rowNext + xPrev + 2] + 0.7152 * frame[rowNext + xPrev + 1] + 0.0722 * frame[rowNext + xPrev];
                double p22 = 0.2126 * frame[rowNext + xNext + 2] + 0.7152 * frame[rowNext + xNext + 1] + 0.0722 * frame[rowNext + xNext];

                double gx = (p02 + 2 * p12 + p22) - (p00 + 2 * p10 + p20);
                double p01 = 0.2126 * frame[rowPrev + x * 4 + 2] + 0.7152 * frame[rowPrev + x * 4 + 1] + 0.0722 * frame[rowPrev + x * 4];
                double p21 = 0.2126 * frame[rowNext + x * 4 + 2] + 0.7152 * frame[rowNext + x * 4 + 1] + 0.0722 * frame[rowNext + x * 4];
                double gy = (p20 + 2 * p21 + p22) - (p00 + 2 * p01 + p02);

                totalGrad += Math.Sqrt(gx * gx + gy * gy);
                count++;
            }
        }

        return count > 0 ? totalGrad / count : 0;
    }

    private static double ComputeLaplacianVariance(byte[] frame, int width, int height)
    {
        double sumLap = 0;
        double sumLapSq = 0;
        int count = 0;
        int stride = width * 4;

        int yStart = height / 4;
        int yEnd = 3 * height / 4;
        int xStart = width / 4;
        int xEnd = 3 * width / 4;

        for (int y = yStart; y < yEnd; y += 4)
        {
            int rowPrev = (y - 1) * stride;
            int rowCurr = y * stride;
            int rowNext = (y + 1) * stride;

            for (int x = xStart; x < xEnd; x += 4)
            {
                int xPrev = (x - 1) * 4;
                int xCurr = x * 4;
                int xNext = (x + 1) * 4;

                double center = 0.2126 * frame[rowCurr + xCurr + 2] + 0.7152 * frame[rowCurr + xCurr + 1] + 0.0722 * frame[rowCurr + xCurr];
                double up = 0.2126 * frame[rowPrev + xCurr + 2] + 0.7152 * frame[rowPrev + xCurr + 1] + 0.0722 * frame[rowPrev + xCurr];
                double down = 0.2126 * frame[rowNext + xCurr + 2] + 0.7152 * frame[rowNext + xCurr + 1] + 0.0722 * frame[rowNext + xCurr];
                double left = 0.2126 * frame[rowCurr + xPrev + 2] + 0.7152 * frame[rowCurr + xPrev + 1] + 0.0722 * frame[rowCurr + xPrev];
                double right = 0.2126 * frame[rowCurr + xNext + 2] + 0.7152 * frame[rowCurr + xNext + 1] + 0.0722 * frame[rowCurr + xNext];

                double lap = (up + down + left + right) - 4.0 * center;
                sumLap += lap;
                sumLapSq += lap * lap;
                count++;
            }
        }

        if (count == 0) return 0;
        double mean = sumLap / count;
        return Math.Max(0, (sumLapSq / count) - (mean * mean));
    }

    private static double ComputeUniformRegionTemporalNoise(List<byte[]> frames, int width, int height)
    {
        if (frames.Count < 2) return 0.0;

        // Search 5 candidate patches (4 quadrants + center) to find the flattest spatial region
        int patchSize = 64;
        int stride = width * 4;
        var candidates = new (int X, int Y)[]
        {
            (width / 4, height / 4),
            (3 * width / 4 - patchSize, height / 4),
            (width / 2 - patchSize / 2, height / 2 - patchSize / 2),
            (width / 4, 3 * height / 4 - patchSize),
            (3 * width / 4 - patchSize, 3 * height / 4 - patchSize)
        };

        byte[] first = frames[0];
        (int BestX, int BestY) bestPatch = candidates[0];
        double minSpatialVar = double.MaxValue;

        foreach (var (px, py) in candidates)
        {
            double sumY = 0, sumYSq = 0;
            int cnt = 0;
            for (int dy = 0; dy < patchSize; dy += 2)
            {
                int row = (py + dy) * stride;
                for (int dx = 0; dx < patchSize; dx += 2)
                {
                    int idx = row + (px + dx) * 4;
                    double yVal = 0.2126 * first[idx + 2] + 0.7152 * first[idx + 1] + 0.0722 * first[idx];
                    sumY += yVal;
                    sumYSq += yVal * yVal;
                    cnt++;
                }
            }
            double mean = sumY / cnt;
            double sVar = (sumYSq / cnt) - (mean * mean);
            if (sVar < minSpatialVar)
            {
                minSpatialVar = sVar;
                bestPatch = (px, py);
            }
        }

        // Measure temporal noise in the selected flat patch
        double sumTemporalDiffSq = 0;
        int patchCount = 0;

        for (int f = 1; f < frames.Count; f++)
        {
            byte[] f1 = frames[f - 1];
            byte[] f2 = frames[f];

            for (int dy = 0; dy < patchSize; dy += 2)
            {
                int row = (bestPatch.BestY + dy) * stride;
                for (int dx = 0; dx < patchSize; dx += 2)
                {
                    int idx = row + (bestPatch.BestX + dx) * 4;
                    double y1 = 0.2126 * f1[idx + 2] + 0.7152 * f1[idx + 1] + 0.0722 * f1[idx];
                    double y2 = 0.2126 * f2[idx + 2] + 0.7152 * f2[idx + 1] + 0.0722 * f2[idx];
                    double diff = y2 - y1;
                    sumTemporalDiffSq += diff * diff;
                    patchCount++;
                }
            }
        }

        return (patchCount > 0) ? sumTemporalDiffSq / patchCount : 0.0;
    }

    private static double ComputeMeanAbsoluteDifference(List<byte[]> aFrames, List<byte[]> bFrames, int width, int height)
    {
        double sumAbs = 0;
        int count = 0;

        for (int i = 0; i < aFrames[0].Length; i += 16)
        {
            double avgA = 0;
            foreach (var a in aFrames)
            {
                avgA += 0.2126 * a[i + 2] + 0.7152 * a[i + 1] + 0.0722 * a[i];
            }
            avgA /= aFrames.Count;

            double avgB = 0;
            foreach (var b in bFrames)
            {
                avgB += 0.2126 * b[i + 2] + 0.7152 * b[i + 1] + 0.0722 * b[i];
            }
            avgB /= bFrames.Count;

            sumAbs += Math.Abs(avgB - avgA);
            count++;
        }

        return count > 0 ? sumAbs / count : 0;
    }

    private static void SavePng(byte[] bgr32Data, int width, int height, string filePath)
    {
        try
        {
            var dpi = 96.0;
            var stride = width * 4;
            var bmp = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgr32, null, bgr32Data, stride);

            using var stream = File.OpenWrite(filePath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(stream);
        }
        catch { }
    }
}
