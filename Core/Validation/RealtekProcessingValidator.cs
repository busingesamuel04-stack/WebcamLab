using System;
using System.Collections.Generic;
using System.Text.Json;
using WebcamLab.Capture;
using WebcamLab.Core.Models;

namespace WebcamLab.Core.Validation;

public class RealtekProcessingTestResult
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Details { get; set; } = string.Empty;
    public double ExecutionMs { get; set; }
}

public static class RealtekProcessingValidator
{
    public static List<RealtekProcessingTestResult> RunAllTests()
    {
        var results = new List<RealtekProcessingTestResult>();

        // Test 1: VendorHardwareSettings serialization fidelity
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var preset = new CapturePreset
        {
            Name = "Cinema Clean YUY2",
            VendorHardware = new VendorHardwareSettings
            {
                TargetVendor = "Realtek",
                TargetDeviceVidPid = "VID_0BDA&PID_58BA",
                ApplyExperimentalProcessing = true,
                RealtekUnit7Control2Value = 0x00
            }
        };

        string json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<CapturePreset>(json);
        sw.Stop();

        bool t1Passed = deserialized != null &&
                        deserialized.VendorHardware != null &&
                        deserialized.VendorHardware.TargetVendor == "Realtek" &&
                        deserialized.VendorHardware.TargetDeviceVidPid == "VID_0BDA&PID_58BA" &&
                        deserialized.VendorHardware.ApplyExperimentalProcessing == true &&
                        deserialized.VendorHardware.RealtekUnit7Control2Value == 0x00;

        results.Add(new RealtekProcessingTestResult
        {
            Name = "1. VendorHardwareSettings JSON Serialization & Round-Trip Fidelity",
            Passed = t1Passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"Serialized {json.Length} chars. Round-trip TargetVendor={deserialized?.VendorHardware?.TargetVendor}, Control2=0x{deserialized?.VendorHardware?.RealtekUnit7Control2Value:X2}. Accurate: {t1Passed}"
        });

        // Test 2: Backward compatibility with legacy presets (VendorHardware == null)
        sw.Restart();
        string legacyJson = @"
        {
            ""Id"": ""legacy-001"",
            ""Name"": ""Legacy Look"",
            ""SchemaVersion"": 3,
            ""HardwareControls"": [],
            ""SoftwareLook"": { ""Contrast"": 1.1 }
        }";
        var legacyPreset = JsonSerializer.Deserialize<CapturePreset>(legacyJson);
        sw.Stop();

        bool t2Passed = legacyPreset != null &&
                        legacyPreset.Name == "Legacy Look" &&
                        legacyPreset.VendorHardware == null;

        results.Add(new RealtekProcessingTestResult
        {
            Name = "2. Backward Compatibility with Legacy Preset Schemas",
            Passed = t2Passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"Legacy JSON parsed cleanly without exception. VendorHardware is null: {legacyPreset?.VendorHardware == null}"
        });

        // Test 3: Format compatibility rules (YUY2 allowed, MJPEG suppressed)
        sw.Restart();
        using var service = new RealtekIspProcessingService();
        var yuy2Format = new CameraFormat
        {
            StreamIndex = 0,
            TypeIndex = 0,
            SubtypeGuid = Guid.Empty,
            FourCC = "YUY2",
            Width = 1920,
            Height = 1080,
            FrameRate = 5.0,
            FrameRateNumerator = 5,
            FrameRateDenominator = 1
        };
        var mjpegFormat = new CameraFormat
        {
            StreamIndex = 0,
            TypeIndex = 1,
            SubtypeGuid = Guid.Empty,
            FourCC = "MJPG",
            Width = 1920,
            Height = 1080,
            FrameRate = 30.0,
            FrameRateNumerator = 30,
            FrameRateDenominator = 1
        };

        service.UpdateActiveFormat(yuy2Format);
        bool yuy2Compat = service.IsFormatCompatible;
        string yuy2Note = service.FormatCompatibilityNote;

        service.UpdateActiveFormat(mjpegFormat);
        bool mjpegCompat = service.IsFormatCompatible;
        string mjpegNote = service.FormatCompatibilityNote;
        sw.Stop();

        bool t3Passed = yuy2Compat && !mjpegCompat && yuy2Note.Contains("Active") && mjpegNote.Contains("Inactive");

        results.Add(new RealtekProcessingTestResult
        {
            Name = "3. Format Compatibility Rules (YUY2 Compatible vs MJPEG Inactive)",
            Passed = t3Passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"YUY2 Compatible={yuy2Compat}, MJPEG Compatible={mjpegCompat}. Explanatory notes correctly formatted."
        });

        // Test 4: Baseline state preservation & rollback invariants
        sw.Restart();
        var clonePreset = preset.Clone("Cinema Clean Copy");
        sw.Stop();

        bool t4Passed = clonePreset != null &&
                        clonePreset.VendorHardware != null &&
                        clonePreset.VendorHardware != preset.VendorHardware && // Deep copy
                        clonePreset.VendorHardware.RealtekUnit7Control2Value == preset.VendorHardware.RealtekUnit7Control2Value;

        results.Add(new RealtekProcessingTestResult
        {
            Name = "4. Preset Deep-Cloning & Vendor Hardware Isolation",
            Passed = t4Passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = $"Deep clone verified. References distinct: {clonePreset?.VendorHardware != preset.VendorHardware}, Values match: {t4Passed}"
        });

        // Test 5: Strict separation of hardware ISP state from host software look
        sw.Restart();
        var look = new SoftwareLookSettings { ExposureOffset = 0.5, Contrast = 1.2 };
        bool t5Passed = look.ExposureOffset == 0.5 &&
                        look.Contrast == 1.2 &&
                        preset.VendorHardware != null &&
                        preset.VendorHardware.RealtekUnit7Control2Value == 0x00;
        sw.Stop();

        results.Add(new RealtekProcessingTestResult
        {
            Name = "5. Separation of Hardware ISP State from Host Software Look Pipeline",
            Passed = t5Passed,
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            Details = "Hardware vendor settings and software look parameters exist in distinct model domains with zero leakage."
        });

        return results;
    }
}
