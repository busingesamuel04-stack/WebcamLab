using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WebcamLab.Core.Models;

namespace WebcamLab.Capture;

/// <summary>
/// Service managing Cinema Look creation, persistence, serialization,
/// keyboard shortcut conflict validation, and 3D LUT integrity checks.
/// Keeps software grading presets completely isolated from hardware camera controls.
/// </summary>
public class CinemaLookService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string LookStorageDirectory { get; }
    public string LutStorageDirectory { get; }

    // System reserved shortcuts that must never be overridden by look hotkeys
    private static readonly HashSet<string> ReservedShortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Space",
        "Ctrl+R",
        "Ctrl+S",
        "Ctrl+G",
        "Ctrl+F",
        "Ctrl+P",
        "Ctrl+M",
        "Escape",
        "F5"
    };

    public CinemaLookService(string? customStorageDir = null, string? customLutDir = null)
    {
        LookStorageDirectory = customStorageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebcamLab",
            "Looks");

        LutStorageDirectory = customLutDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebcamLab",
            "LUTs");

        try
        {
            if (!Directory.Exists(LookStorageDirectory))
            {
                Directory.CreateDirectory(LookStorageDirectory);
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
    /// Factory providing standard factory-calibrated Cinema Looks.
    /// </summary>
    public static IReadOnlyList<CinemaLook> GetBuiltInLooks()
    {
        return
        [
            new CinemaLook
            {
                Id = "look-neutral-bypass",
                Name = "Neutral Bypass",
                Description = "Untouched linear passthrough without software color grading or filtering.",
                IsBuiltIn = true,
                ShortcutKey = "Ctrl+0",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Neutral Bypass",
                    ToneCurveName = "Linear (Passthrough)",
                    ExposureOffset = 0.0,
                    Contrast = 1.0,
                    Saturation = 1.0,
                    Highlights = 0.0,
                    Shadows = 0.0,
                    SCurveStrength = 0.0,
                    SharpenStrength = 0.0,
                    DenoiseStrength = 0.0,
                    LutEnabled = false
                }
            },
            new CinemaLook
            {
                Id = "look-subtle-film-contrast",
                Name = "Subtle Film Contrast",
                Description = "Gentle film curve with 15% contrast boost, smooth roll-off, and subtle unsharp masking.",
                IsBuiltIn = true,
                ShortcutKey = "Ctrl+1",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Subtle Film Contrast",
                    ToneCurveName = "Film S-Curve",
                    ExposureOffset = 0.0,
                    Contrast = 1.15,
                    Saturation = 1.05,
                    Highlights = -5.0,
                    Shadows = -10.0,
                    SCurveStrength = 0.35,
                    SharpenStrength = 0.20,
                    DenoiseStrength = 0.0,
                    LutEnabled = false
                }
            },
            new CinemaLook
            {
                Id = "look-warm-amber-cinema",
                Name = "Warm Amber Cinema",
                Description = "Golden highlights, warm shadow lift, and rich skin tone warmth using 3D warm film LUT.",
                IsBuiltIn = true,
                ShortcutKey = "Ctrl+2",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Warm Amber Cinema",
                    ToneCurveName = "Warm Film S-Curve",
                    ExposureOffset = 0.0,
                    Contrast = 1.10,
                    Saturation = 1.05,
                    Highlights = 0.0,
                    Shadows = -5.0,
                    SCurveStrength = 0.25,
                    SharpenStrength = 0.20,
                    DenoiseStrength = 0.0,
                    LutFileName = "film_warmth.cube",
                    LutEnabled = true
                }
            },
            new CinemaLook
            {
                Id = "look-cool-steel-cinema",
                Name = "Cool Steel Cinema",
                Description = "Crisp steel cyan shadows and neutral highlights with edge enhancement.",
                IsBuiltIn = true,
                ShortcutKey = "Ctrl+3",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Cool Steel Cinema",
                    ToneCurveName = "Steel Cool Curve",
                    ExposureOffset = 0.0,
                    Contrast = 1.15,
                    Saturation = 0.95,
                    Highlights = 0.0,
                    Shadows = -5.0,
                    SCurveStrength = 0.30,
                    SharpenStrength = 0.25,
                    DenoiseStrength = 0.0,
                    LutFileName = "cool_steel.cube",
                    LutEnabled = true
                }
            },
            new CinemaLook
            {
                Id = "look-punchy-commercial",
                Name = "Punchy Commercial",
                Description = "Vibrant commercial grade with lifted shadows, controlled highlights, and 20% saturation boost.",
                IsBuiltIn = true,
                ShortcutKey = "Ctrl+4",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Punchy Commercial",
                    ToneCurveName = "High Dynamic S-Curve",
                    ExposureOffset = 0.20,
                    Contrast = 1.25,
                    Saturation = 1.20,
                    Highlights = -10.0,
                    Shadows = 10.0,
                    SCurveStrength = 0.40,
                    SharpenStrength = 0.30,
                    DenoiseStrength = 0.0,
                    LutEnabled = false
                }
            },
            new CinemaLook
            {
                Id = "look-clean-black-and-white",
                Name = "Clean Black & White",
                Description = "Classic high-contrast monochrome with deep blacks and sharp micro-contrast.",
                IsBuiltIn = true,
                ShortcutKey = "Ctrl+5",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Clean Black & White",
                    ToneCurveName = "Monochrome Contrast Curve",
                    ExposureOffset = 0.0,
                    Contrast = 1.30,
                    Saturation = 0.0,
                    Highlights = -5.0,
                    Shadows = -15.0,
                    SCurveStrength = 0.50,
                    SharpenStrength = 0.30,
                    DenoiseStrength = 0.0,
                    LutEnabled = false
                }
            }
        ];
    }

    /// <summary>
    /// Loads all user-created looks from persistent storage directory.
    /// </summary>
    public List<CinemaLook> LoadUserLooks()
    {
        var list = new List<CinemaLook>();
        if (!Directory.Exists(LookStorageDirectory)) return list;

        foreach (var file in Directory.EnumerateFiles(LookStorageDirectory, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var look = JsonSerializer.Deserialize<CinemaLook>(json, JsonOptions);
                if (look != null && !string.IsNullOrWhiteSpace(look.Name))
                {
                    look.IsBuiltIn = false;
                    look.Settings ??= new SoftwareLookSettings();
                    look.SchemaVersion = CinemaLook.CurrentSchemaVersion;
                    list.Add(look);
                }
            }
            catch
            {
                // Skip corrupted or unreadable look files
            }
        }

        return list.OrderBy(l => l.Name).ToList();
    }

    /// <summary>
    /// Returns combined list of built-in starter looks and user looks.
    /// </summary>
    public List<CinemaLook> GetAllLooks()
    {
        var all = new List<CinemaLook>(GetBuiltInLooks());
        all.AddRange(LoadUserLooks());
        return all;
    }

    /// <summary>
    /// Saves or updates a user cinema look in persistent storage.
    /// </summary>
    public void SaveUserLook(CinemaLook look)
    {
        if (look.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in looks cannot be overwritten. Duplicate the look to modify it.");
        }

        look.ModifiedAt = DateTime.UtcNow;
        string safeFileName = GetSafeFileName(look.Id);
        string filePath = Path.Combine(LookStorageDirectory, $"{safeFileName}.json");

        string json = JsonSerializer.Serialize(look, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Renames an existing user cinema look.
    /// </summary>
    public void RenameUserLook(CinemaLook look, string newName)
    {
        if (look.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in looks cannot be renamed.");
        }
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Look name cannot be empty.", nameof(newName));
        }

        look.Name = newName.Trim();
        look.Settings.LookName = look.Name;
        SaveUserLook(look);
    }

    /// <summary>
    /// Clones an existing look with a new name and unique ID.
    /// </summary>
    public CinemaLook DuplicateLook(CinemaLook source, string? newName = null)
    {
        var clone = source.Clone(newName);
        SaveUserLook(clone);
        return clone;
    }

    /// <summary>
    /// Deletes a user cinema look from persistent storage.
    /// </summary>
    public bool DeleteUserLook(CinemaLook look)
    {
        if (look.IsBuiltIn) return false;

        string safeFileName = GetSafeFileName(look.Id);
        string filePath = Path.Combine(LookStorageDirectory, $"{safeFileName}.json");

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Exports a look to an external JSON file.
    /// </summary>
    public void ExportLook(CinemaLook look, string filePath)
    {
        string json = JsonSerializer.Serialize(look, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Imports a look from an external JSON file.
    /// </summary>
    public CinemaLook ImportLook(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var look = JsonSerializer.Deserialize<CinemaLook>(json, JsonOptions)
                   ?? throw new InvalidDataException("Invalid Cinema Look file format.");

        if (string.IsNullOrWhiteSpace(look.Name))
        {
            look.Name = Path.GetFileNameWithoutExtension(filePath);
        }

        look.Id = Guid.NewGuid().ToString("N");
        look.IsBuiltIn = false;
        look.ShortcutKey = null; // Do not import hotkey to prevent collisions
        look.Settings ??= new SoftwareLookSettings();
        look.SchemaVersion = CinemaLook.CurrentSchemaVersion;
        look.CreatedAt = DateTime.UtcNow;
        look.ModifiedAt = DateTime.UtcNow;

        SaveUserLook(look);
        return look;
    }

    /// <summary>
    /// Validates whether a shortcut is permitted, checking against reserved system shortcuts
    /// and other existing look shortcuts to prevent hotkey collisions.
    /// </summary>
    public bool ValidateShortcut(
        string shortcut,
        string? currentLookId,
        IEnumerable<CinemaLook> allLooks,
        out string conflictReason)
    {
        conflictReason = string.Empty;
        if (string.IsNullOrWhiteSpace(shortcut)) return true;

        string normalized = shortcut.Trim();

        // 1. Check against system-reserved hotkeys
        if (ReservedShortcuts.Contains(normalized))
        {
            conflictReason = $"Shortcut '{normalized}' is reserved by the application (recording, playback, or camera HUD).";
            return false;
        }

        // 2. Check for duplicate assignment across looks
        foreach (var other in allLooks)
        {
            if (other.Id != currentLookId &&
                !string.IsNullOrWhiteSpace(other.ShortcutKey) &&
                string.Equals(other.ShortcutKey.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                conflictReason = $"Shortcut '{normalized}' is already assigned to look '{other.Name}'.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Inspects and validates a 3D LUT referenced by look settings.
    /// Gracefully reports missing or incompatible LUTs without silent substitution.
    /// </summary>
    public (LutStatus Status, string? ResolvedPath, string Message) ResolveAndValidateLut(SoftwareLookSettings settings)
    {
        if (!settings.LutEnabled || (string.IsNullOrWhiteSpace(settings.LutFilePath) && string.IsNullOrWhiteSpace(settings.LutFileName)))
        {
            return (LutStatus.None, null, "No 3D LUT configured.");
        }

        string? candidatePath = null;

        // 1. Direct path check
        if (!string.IsNullOrEmpty(settings.LutFilePath) && File.Exists(settings.LutFilePath))
        {
            candidatePath = settings.LutFilePath;
        }
        else if (!string.IsNullOrEmpty(settings.LutFileName))
        {
            // 2. Check user LUT storage directory
            string userCandidate = Path.Combine(LutStorageDirectory, settings.LutFileName);
            if (File.Exists(userCandidate))
            {
                candidatePath = userCandidate;
            }
            else
            {
                // 3. Check application install directory
                string appCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Presets", "LUTs", settings.LutFileName);
                if (File.Exists(appCandidate))
                {
                    candidatePath = Path.GetFullPath(appCandidate);
                }
                else
                {
                    // 4. Check project relative tree
                    string projectCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Presets", "LUTs", settings.LutFileName);
                    if (File.Exists(projectCandidate))
                    {
                        candidatePath = Path.GetFullPath(projectCandidate);
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(candidatePath) || !File.Exists(candidatePath))
        {
            string lutName = settings.LutFileName ?? Path.GetFileName(settings.LutFilePath ?? "unknown.cube");
            return (LutStatus.Missing, null, $"LUT file '{lutName}' not found on disk. Tone adjustments active, LUT stage bypassed without substitution.");
        }

        // Validate LUT parsing
        try
        {
            var lut = CubeLut.LoadFromFile(candidatePath);
            if (!lut.IsLoaded)
            {
                return (LutStatus.Incompatible, candidatePath, $"LUT '{Path.GetFileName(candidatePath)}' could not be loaded into memory.");
            }

            return (LutStatus.Ready, candidatePath, $"LUT '{lut.Title}' ({lut.Type} {lut.Size}³) verified.");
        }
        catch (Exception ex)
        {
            return (LutStatus.Incompatible, candidatePath, $"LUT '{Path.GetFileName(candidatePath)}' format error: {ex.Message}. Bypassed without substitution.");
        }
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
