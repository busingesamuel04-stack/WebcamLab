using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebcamLab.Core.Models;

namespace WebcamLab.Capture;

public interface ICameraService : IDisposable
{
    bool IsCapturing { get; }
    bool IsRecording { get; }
    bool IsFinalizing { get; }
    bool IsBurstCapturing { get; }
    CaptureStatistics Statistics { get; }
    CameraControlService Controls { get; }
    RealtekIspProcessingService RealtekIsp { get; }
    RecordingMode SelectedRecordingMode { get; set; }
    RecordingCadenceMode SelectedCadenceMode { get; set; }
    RecordingBackendMode SelectedRecordingBackend { get; set; }
    RecordingTelemetry GetRecordingTelemetry();
    void WriteRecordingSidecarV3(string mp4Path, bool isOriginalMode = false);

    bool EnableAnalysis { get; set; }
    bool EnableZebra { get; set; }
    int ZebraThreshold { get; set; }
    bool IsFullRange { get; set; }

    SoftwareLookSettings LookSettings { get; set; }
    CubeLut? ActiveLut { get; set; }
    bool IsGradedPreviewActive { get; set; }
    SoftwareLookPipeline LookPipeline { get; }

    FalseColorSettings FalseColorSettings { get; set; }
    FalseColorEngine FalseColorEngine { get; }
    void UpdateFalseColorSettings(FalseColorSettings settings);

    WebcamLab.Capture.Audio.WasapiCaptureService AudioCapture { get; }
    WebcamLab.Capture.Audio.AudioVideoSyncReport? LastSyncReport { get; }

    event Action<IntPtr, int, int, int>? FrameReady;
    event Action<HistogramData>? HistogramReady;
    event Action<CaptureStatistics>? StatisticsUpdated;
    event Action<string>? ErrorOccurred;
    event Action<int, int>? BurstProgressUpdated;

    Task<IReadOnlyList<CameraDevice>> EnumerateCamerasAsync();
    Task<IReadOnlyList<CameraFormat>> GetSupportedFormatsAsync(CameraDevice device);
    Task StartCaptureAsync(CameraDevice device, CameraFormat format);
    Task StopCaptureAsync();
    Task<string> CaptureStillImageAsync(string outputDirectory, string format = "png");
    Task<string> ExportProcessedSnapshotAsync(string outputDirectory, SoftwareLookSettings settings, string format = "png");
    Task<MultiFrameEnhancementSidecar> CaptureEnhancedStillAsync(string outputDirectory, MultiFrameBurstSettings settings);
    Task StartRecordingAsync(
        string outputFilePath,
        RecordingMode mode = RecordingMode.OriginalCamera,
        RecordingCadenceMode cadenceMode = RecordingCadenceMode.CFR_Compatibility,
        bool enableAudio = true,
        WebcamLab.Core.Audio.AudioDeviceInfo? audioDevice = null);
    Task StopRecordingAsync();
    IReadOnlyList<FrameTimingAuditRecord> GetLastRecordingAuditRecords();
    void WriteTimingAuditSidecar(string sidecarPath);
    int LastMonotonicityCorrectionsCount { get; }
}
