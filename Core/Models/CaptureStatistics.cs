using System;

namespace WebcamLab.Core.Models;

public class CaptureStatistics
{
    public string ActiveResolution { get; set; } = "None";
    public string ActivePixelFormat { get; set; } = "None";
    public double TargetFps { get; set; }
    public double CurrentFps { get; set; }
    public long TotalFrames { get; set; }
    public long DroppedFrames { get; set; }
    public double DataRateMBps { get; set; }
    public string CaptureBackend { get; set; } = "Windows Media Foundation (SourceReader)";
    public bool IsActive { get; set; }
    public bool IsRecording { get; set; }
    public TimeSpan RecordingDuration { get; set; } = TimeSpan.Zero;
    public string? RecordingFilePath { get; set; }
}
