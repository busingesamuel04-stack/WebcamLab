namespace WebcamLab.Core.Models;

public class CameraDevice
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required string SymbolicLink { get; init; }
    public bool IsDellUZ2315H { get; init; }
    public string? VendorId { get; init; }
    public string? ProductId { get; init; }

    public string DisplayName => IsDellUZ2315H
        ? $"{Name} [Dell UZ2315H - VID:{VendorId} PID:{ProductId}]"
        : $"{Name} ({VendorId ?? "Generic"})";

    public override string ToString() => DisplayName;
}
