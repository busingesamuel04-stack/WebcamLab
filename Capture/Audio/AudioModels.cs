using System;

namespace WebcamLab.Capture.Audio;

public record AudioChunk(
    byte[] Pcm16Data,
    int SampleCount,
    int Channels,
    int SampleRate,
    ulong DevicePosition,
    ulong QPCPosition,
    uint Flags);

public class AudioLevelEventArgs : EventArgs
{
    public float PeakLevel { get; init; } // 0.0 to 1.0
    public float PeakDb { get; init; }    // -60 to 0 dBFS
    public float RmsLevel { get; init; }  // 0.0 to 1.0
    public float RmsDb { get; init; }     // -60 to 0 dBFS
    public bool IsClipping { get; init; }
    public int DiscontinuitiesCount { get; init; }
}

public class AudioCaptureStats
{
    public long TotalFramesRead { get; set; }
    public long TotalBytesRecorded { get; set; }
    public int DiscontinuitiesCount { get; set; }
    public int SilencePacketsCount { get; set; }
    public ulong InitialQpcPosition { get; set; }
    public ulong FinalQpcPosition { get; set; }
    public ulong InitialDevicePosition { get; set; }
    public ulong FinalDevicePosition { get; set; }
    public double DurationSeconds { get; set; }
    public double EstimatedSampleRate { get; set; }
}
