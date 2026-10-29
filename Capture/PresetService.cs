using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WebcamLab.Core.Models;
using WebcamLab.Core.Native;

namespace WebcamLab.Capture;

/// <summary>
/// Service managing capture preset creation, persistence, serialization,
/// and safe hardware application with full readback verification.
/// </summary>
public class PresetService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string PresetStorageDirectory { get; }
    public string LutStorageDirectory { get; }

    public PresetService(string? customStorageDir = null)
    {
        PresetStorageDirectory = customStorageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebcamLab",
            "Presets");

        LutStorageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebcamLab",
            "LUTs");

        try
        {
            if (!Directory.Exists(PresetStorageDirectory))
            {
                Directory.CreateDirectory(PresetStorageDirectory);
            }
            if (!Directory.Exists(LutStorageDirectory))
            {
                Directory.CreateDirectory(LutStorageDirectory);
            }
            CubeLut.EnsureStandardLuts(LutStorageDirectory);

            string projectLutDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Presets", "LUTs");
            if (Directory.Exists(Path.GetDirectoryName(projectLutDir)))
            {
                CubeLut.EnsureStandardLuts(Path.GetFullPath(projectLutDir));
            }
        }
        catch { }
    }

    /// <summary>
    /// Factory providing the standard starter profiles tailored to Realtek RTS5875 hardware limits.
    /// </summary>
    public static IReadOnlyList<CapturePreset> GetBuiltInPresets()
    {
        return
        [
            new CapturePreset
            {
                Id = "builtin-natural-neutral",
                Name = "Natural / Neutral",
                Description = "Balanced, natural color rendition using standard neutral camera tuning.",
                IsBuiltIn = true,
                HardwareControls =
                [
                    new PresetControlValue { PropertyName = "Brightness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Brightness, Value = 0, IsAuto = false },
                    new PresetControlValue { PropertyName = "Contrast", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Contrast, Value = 15, IsAuto = false },
                    new PresetControlValue { PropertyName = "Saturation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Saturation, Value = 64, IsAuto = false },
                    new PresetControlValue { PropertyName = "Sharpness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Sharpness, Value = 2, IsAuto = false },
                    new PresetControlValue { PropertyName = "Gamma", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Gamma, Value = 100, IsAuto = false },
                    new PresetControlValue { PropertyName = "White Balance (Kelvin)", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.WhiteBalance, Value = 4600, IsAuto = true },
                    new PresetControlValue { PropertyName = "Backlight Compensation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.BacklightCompensation, Value = 1, IsAuto = false }
                ],
                SoftwareLook = new SoftwareLookSettings
                {
                    LookName = "Neutral (Bypass)",
                    Enabled = true,
                    Contrast = 1.0,
                    Saturation = 1.0
                }
            },
            new CapturePreset
            {
                Id = "builtin-warm-studio",
                Name = "Warm Studio",
                Description = "Warm indoor studio profile with manual 3800K white balance and rich saturation.",
                IsBuiltIn = true,
                HardwareControls =
                [
                    new PresetControlValue { PropertyName = "Brightness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Brightness, Value = -5, IsAuto = false },
                    new PresetControlValue { PropertyName = "Contrast", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Contrast, Value = 20, IsAuto = false },
                    new PresetControlValue { PropertyName = "Saturation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Saturation, Value = 70, IsAuto = false },
                    new PresetControlValue { PropertyName = "Sharpness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Sharpness, Value = 2, IsAuto = false },
                    new PresetControlValue { PropertyName = "Gamma", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Gamma, Value = 110, IsAuto = false },
                    new PresetControlValue { PropertyName = "White Balance (Kelvin)", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.WhiteBalance, Value = 3800, IsAuto = false },
                    new PresetControlValue { PropertyName = "Backlight Compensation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.BacklightCompensation, Value = 1, IsAuto = false }
                ],
                SoftwareLook = new SoftwareLookSettings
                {
                    LookName = "Warm Amber Cinema",
                    Enabled = true,
                    Contrast = 1.10,
                    Saturation = 1.05,
                    SCurveStrength = 0.20,
                    LutFileName = "film_warmth.cube",
                    LutEnabled = true
                }
            },
            new CapturePreset
            {
                Id = "builtin-cool-daylight",
                Name = "Cool Daylight",
                Description = "Crisp, cool daylight profile with elevated manual color temperature (6000K).",
                IsBuiltIn = true,
                HardwareControls =
                [
                    new PresetControlValue { PropertyName = "Brightness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Brightness, Value = -5, IsAuto = false },
                    new PresetControlValue { PropertyName = "Contrast", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Contrast, Value = 18, IsAuto = false },
                    new PresetControlValue { PropertyName = "Saturation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Saturation, Value = 60, IsAuto = false },
                    new PresetControlValue { PropertyName = "Sharpness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Sharpness, Value = 3, IsAuto = false },
                    new PresetControlValue { PropertyName = "Gamma", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Gamma, Value = 100, IsAuto = false },
                    new PresetControlValue { PropertyName = "White Balance (Kelvin)", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.WhiteBalance, Value = 6000, IsAuto = false },
                    new PresetControlValue { PropertyName = "Backlight Compensation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.BacklightCompensation, Value = 1, IsAuto = false }
                ],
                SoftwareLook = new SoftwareLookSettings
                {
                    LookName = "Cool Steel Cinema",
                    Enabled = true,
                    Contrast = 1.10,
                    Saturation = 0.95,
                    SCurveStrength = 0.20,
                    LutFileName = "cool_steel.cube",
                    LutEnabled = true
                }
            },
            new CapturePreset
            {
                Id = "builtin-high-contrast",
                Name = "High Contrast",
                Description = "Punchy profile with deeper blacks, boosted midtone gamma, and crisp contrast.",
                IsBuiltIn = true,
                HardwareControls =
                [
                    new PresetControlValue { PropertyName = "Brightness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Brightness, Value = -15, IsAuto = false },
                    new PresetControlValue { PropertyName = "Contrast", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Contrast, Value = 35, IsAuto = false },
                    new PresetControlValue { PropertyName = "Saturation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Saturation, Value = 75, IsAuto = false },
                    new PresetControlValue { PropertyName = "Sharpness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Sharpness, Value = 4, IsAuto = false },
                    new PresetControlValue { PropertyName = "Gamma", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Gamma, Value = 130, IsAuto = false },
                    new PresetControlValue { PropertyName = "White Balance (Kelvin)", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.WhiteBalance, Value = 5000, IsAuto = true },
                    new PresetControlValue { PropertyName = "Backlight Compensation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.BacklightCompensation, Value = 0, IsAuto = false }
                ],
                SoftwareLook = new SoftwareLookSettings
                {
                    LookName = "High Contrast Punch",
                    Enabled = true,
                    Contrast = 1.25,
                    SCurveStrength = 0.40,
                    SharpenStrength = 0.25
                }
            },
            new CapturePreset
            {
                Id = "builtin-monochrome",
                Name = "Monochrome",
                Description = "Camera-processed monochrome output achieved by setting hardware saturation to zero (not physical sensor black-and-white data).",
                IsBuiltIn = true,
                HardwareControls =
                [
                    new PresetControlValue { PropertyName = "Brightness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Brightness, Value = -10, IsAuto = false },
                    new PresetControlValue { PropertyName = "Contrast", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Contrast, Value = 25, IsAuto = false },
                    new PresetControlValue { PropertyName = "Saturation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Saturation, Value = 0, IsAuto = false }, // Hardware Chroma Zero!
                    new PresetControlValue { PropertyName = "Sharpness", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Sharpness, Value = 3, IsAuto = false },
                    new PresetControlValue { PropertyName = "Gamma", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.Gamma, Value = 120, IsAuto = false },
                    new PresetControlValue { PropertyName = "White Balance (Kelvin)", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.WhiteBalance, Value = 4600, IsAuto = true },
                    new PresetControlValue { PropertyName = "Backlight Compensation", PropertyType = HardwarePropertyType.VideoProcAmp, PropertyId = (int)VideoProcAmpProperty.BacklightCompensation, Value = 1, IsAuto = false }
                ],
                SoftwareLook = new SoftwareLookSettings
                {
                    LookName = "Rich Monochrome",
                    Enabled = true,
                    Contrast = 1.20,
                    Saturation = 0.0,
                    SCurveStrength = 0.35,
                    SharpenStrength = 0.20
                }
            }
        ];
    }

    /// <summary>
    /// Loads all user-created presets from persistent storage directory.
    /// </summary>
    public List<CapturePreset> LoadUserPresets()
    {
        var list = new List<CapturePreset>();
        if (!Directory.Exists(PresetStorageDirectory)) return list;

        foreach (var file in Directory.EnumerateFiles(PresetStorageDirectory, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var preset = JsonSerializer.Deserialize<CapturePreset>(json, JsonOptions);
                if (preset != null && !string.IsNullOrWhiteSpace(preset.Name))
                {
                    preset.IsBuiltIn = false; // User presets are never built-in
                    if (preset.SoftwareLook == null)
                    {
                        preset.SoftwareLook = new SoftwareLookSettings();
                    }
                    if (preset.FalseColor == null)
                    {
                        preset.FalseColor = new FalseColorSettings();
                    }
                    preset.SchemaVersion = CapturePreset.CurrentSchemaVersion;
                    list.Add(preset);
                }
            }
            catch
            {
                // Skip corrupted or unreadable preset files
            }
        }

        return list.OrderBy(p => p.Name).ToList();
    }

    /// <summary>
    /// Saves or updates a user preset in persistent storage.
    /// </summary>
    public void SaveUserPreset(CapturePreset preset)
    {
        if (preset.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in presets cannot be overwritten. Duplicate the preset to edit.");
        }

        preset.ModifiedAt = DateTime.UtcNow;
        string safeFileName = GetSafeFileName(preset.Id);
        string filePath = Path.Combine(PresetStorageDirectory, $"{safeFileName}.json");

        string json = JsonSerializer.Serialize(preset, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Deletes a user preset from persistent storage.
    /// </summary>
    public bool DeleteUserPreset(CapturePreset preset)
    {
        if (preset.IsBuiltIn) return false;

        string safeFileName = GetSafeFileName(preset.Id);
        string filePath = Path.Combine(PresetStorageDirectory, $"{safeFileName}.json");

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Exports a preset to an external JSON file.
    /// </summary>
    public void ExportPreset(CapturePreset preset, string filePath)
    {
        string json = JsonSerializer.Serialize(preset, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Imports a preset from an external JSON file.
    /// </summary>
    public CapturePreset ImportPreset(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var preset = JsonSerializer.Deserialize<CapturePreset>(json, JsonOptions)
                     ?? throw new InvalidDataException("Invalid preset file format.");

        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            preset.Name = Path.GetFileNameWithoutExtension(filePath);
        }

        // Give imported preset a fresh unique ID and mark as user preset
        preset.Id = Guid.NewGuid().ToString("N");
        preset.IsBuiltIn = false;
        if (preset.SoftwareLook == null)
        {
            preset.SoftwareLook = new SoftwareLookSettings();
        }
        if (preset.FalseColor == null)
        {
            preset.FalseColor = new FalseColorSettings();
        }
        preset.SchemaVersion = CapturePreset.CurrentSchemaVersion;
        preset.CreatedAt = DateTime.UtcNow;
        preset.ModifiedAt = DateTime.UtcNow;

        return preset;
    }

    /// <summary>
    /// Creates a new CapturePreset snapshot from currently supported hardware properties.
    /// </summary>
    public CapturePreset CreateFromCurrentSettings(string name, string description, IEnumerable<CameraHardwareProperty> properties)
    {
        var preset = new CapturePreset
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = description,
            IsBuiltIn = false,
            SchemaVersion = CapturePreset.CurrentSchemaVersion,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            SoftwareLook = new SoftwareLookSettings(),
            FalseColor = new FalseColorSettings()
        };

        foreach (var p in properties.Where(p => p.IsSupported))
        {
            preset.HardwareControls.Add(new PresetControlValue(p));
        }

        return preset;
    }

    /// <summary>
    /// Safely applies preset settings to hardware with read-back confirmation.
    /// Skips unchanged settings to avoid unnecessary USB transactions.
    /// Explicitly reports confirmed, skipped, unsupported, and failed controls.
    /// </summary>
    public PresetApplyResult ApplyPreset(
        CapturePreset preset,
        IReadOnlyList<CameraHardwareProperty> currentProperties,
        CameraControlService controlService)
    {
        var result = new PresetApplyResult
        {
            PresetName = preset.Name
        };

        var propLookup = currentProperties.ToDictionary(
            p => (p.PropertyType, p.PropertyId),
            p => p);

        int applied = 0;
        int skipped = 0;
        int unsupported = 0;
        int failed = 0;

        foreach (var target in preset.HardwareControls)
        {
            if (!propLookup.TryGetValue((target.PropertyType, target.PropertyId), out var liveProp) || !liveProp.IsSupported)
            {
                unsupported++;
                result.UnsupportedDetails.Add($"{target.PropertyName}: Not supported on this device.");
                continue;
            }

            // Optimization: If property is already at the target value and auto mode, skip write
            if (liveProp.CurrentValue == target.Value && liveProp.IsAuto == target.IsAuto)
            {
                skipped++;
                result.SkippedDetails.Add($"{liveProp.Name}: Already at target ({target.Value}, Auto={target.IsAuto}).");
                continue;
            }

            // Write setting to hardware and verify read-back
            bool ok = controlService.SetProperty(liveProp, target.Value, target.IsAuto);
            if (ok)
            {
                applied++;
                result.ConfirmedDetails.Add($"{liveProp.Name}: Confirmed {liveProp.CurrentValue} (Auto={liveProp.IsAuto}).");
            }
            else
            {
                failed++;
                result.FailedDetails.Add($"{liveProp.Name}: Write failed ({liveProp.StatusMessage}).");
            }
        }

        bool success = failed == 0;
        string summary = $"Preset '{preset.Name}': {applied} confirmed, {skipped} unchanged, {unsupported} unsupported, {failed} failed.";

        return new PresetApplyResult
        {
            PresetName = preset.Name,
            Success = success,
            AppliedCount = applied,
            SkippedUnchangedCount = skipped,
            UnsupportedCount = unsupported,
            FailedCount = failed,
            Summary = summary
        };
    }

    /// <summary>
    /// Evaluates whether the camera's live hardware settings differ from the target values in the preset.
    /// </summary>
    public bool IsPresetModified(CapturePreset preset, IEnumerable<CameraHardwareProperty> currentProperties)
    {
        var propLookup = currentProperties
            .Where(p => p.IsSupported)
            .ToDictionary(p => (p.PropertyType, p.PropertyId), p => p);

        foreach (var target in preset.HardwareControls)
        {
            if (propLookup.TryGetValue((target.PropertyType, target.PropertyId), out var liveProp))
            {
                if (liveProp.CurrentValue != target.Value || liveProp.IsAuto != target.IsAuto)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetSafeFileName(string id)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            id = id.Replace(c, '_');
        }
        return id;
    }
}
