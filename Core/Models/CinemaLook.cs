using System;
using System.Text.Json.Serialization;

namespace WebcamLab.Core.Models;

/// <summary>
/// Status of a 3D LUT asset referenced by a Cinema Look.
/// </summary>
public enum LutStatus
{
    None = 0,
    Ready = 1,
    Missing = 2,
    Incompatible = 3
}

/// <summary>
/// A named, reusable software look profile representing color grading,
/// tone curve adjustments, and 3D LUT configuration.
/// Kept strictly isolated from hardware camera controls and recording cadence.
/// </summary>
public class CinemaLook
{
    public const int CurrentSchemaVersion = 3;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; } = false;
    public string? ShortcutKey { get; set; } = null; // e.g. "Ctrl+0", "Ctrl+1"
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Software look parameters (tone curves, exposure, contrast, saturation, LUT, spatial filters).
    /// </summary>
    public SoftwareLookSettings Settings { get; set; } = new();

    /// <summary>
    /// Runtime LUT availability status (not persisted directly to look schema).
    /// </summary>
    [JsonIgnore]
    public LutStatus LutAvailability { get; set; } = LutStatus.None;

    [JsonIgnore]
    public string LutStatusMessage { get; set; } = string.Empty;

    public CinemaLook Clone(string? newName = null)
    {
        return new CinemaLook
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = newName ?? $"{Name} (Copy)",
            Description = Description,
            IsBuiltIn = false, // Clones are always user-editable
            ShortcutKey = null, // Clones do not inherit shortcut to prevent conflict
            SchemaVersion = SchemaVersion,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            Settings = Settings.Clone()
        };
    }

    public override string ToString() => string.IsNullOrWhiteSpace(ShortcutKey)
        ? Name
        : $"{Name} [{ShortcutKey}]";
}
