using System;

namespace WebcamLab.Core.Audio;

public class AudioDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public int Channels { get; set; } = 2;
    public int SampleRate { get; set; } = 48000;
    public int BitsPerSample { get; set; } = 16;
    public bool IsFloat { get; set; }
    public bool IsDellUZ2315H { get; set; }

    public string FormatSummary => $"{SampleRate} Hz, {Channels} Ch ({(Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels}-Ch")}), {BitsPerSample}-bit {(IsFloat ? "Float" : "PCM")}";

    public string DisplayName => IsDefault ? $"{FriendlyName} (Default)" : FriendlyName;

    public override string ToString() => $"{DisplayName} [{FormatSummary}]";
}
