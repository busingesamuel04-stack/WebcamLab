using System;
using System.Collections.Generic;

namespace WebcamLab.Core.Models;

/// <summary>
/// Detailed, transparent telemetry report returned after applying a preset to hardware.
/// </summary>
public class PresetApplyResult
{
    public string PresetName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public int AppliedCount { get; init; }
    public int SkippedUnchangedCount { get; init; }
    public int UnsupportedCount { get; init; }
    public int FailedCount { get; init; }

    public List<string> ConfirmedDetails { get; } = [];
    public List<string> SkippedDetails { get; } = [];
    public List<string> UnsupportedDetails { get; } = [];
    public List<string> FailedDetails { get; } = [];

    public string Summary { get; init; } = string.Empty;
}
