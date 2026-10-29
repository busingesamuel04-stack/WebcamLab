using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using WebcamLab.Core.Models;

namespace WebcamLab.Capture;

/// <summary>
/// High-performance, zero-allocation false-color analysis and rendering engine.
/// Renders a non-destructive exposure visualization overlay onto a dedicated presentation buffer.
/// Does not modify camera capture frames, original JPEG snapshots, or MP4 recordings.
/// </summary>
public class FalseColorEngine
{
    // Pre-computed 256-entry lookup tables for 8-bit BT.709 luma [0..255]
    private readonly uint[] _colorLut = new uint[256];
    private readonly bool[] _isMappedLut = new bool[256];
    private readonly byte[] _lutR = new byte[256];
    private readonly byte[] _lutG = new byte[256];
    private readonly byte[] _lutB = new byte[256];

    private FalseColorProfile _cachedProfile = (FalseColorProfile)(-1);
    private ArriIntermediateMode _cachedIntermediateMode = (ArriIntermediateMode)(-1);
    private int _cachedZoneHash = 0;

    /// <summary>
    /// Evaluates which zone a given IRE percentage [0.0..100.0] falls into, along with its mapped BGR32 color.
    /// </summary>
    public static FalseColorZone? EvaluateZone(double ire, FalseColorSettings settings, out uint bgrColor, out bool isMapped)
    {
        ire = Math.Clamp(ire, 0.0, 100.0);
        bgrColor = 0xFF808080u; // Default mid-gray
        isMapped = false;

        if (settings.SelectedProfile == FalseColorProfile.ArriStyleReference)
        {
            var arriBands = FalseColorSettings.CreateArriReferenceZones();
            // SmallHD ARRI Reference bands:
            // 0: Purple 0 - 2.5 IRE
            // 1: Blue 2.5 - 4.0 IRE
            // 2: Green 38.0 - 42.0 IRE
            // 3: Pink 52.0 - 56.0 IRE
            // 4: Yellow 97.0 - 99.0 IRE
            // 5: Red 99.0 - 100.0 IRE
            if (ire <= 2.5)
            {
                bgrColor = arriBands[0].Bgr32Color;
                isMapped = true;
                return arriBands[0];
            }
            if (ire > 2.5 && ire <= 4.0)
            {
                bgrColor = arriBands[1].Bgr32Color;
                isMapped = true;
                return arriBands[1];
            }
            if (ire >= 38.0 && ire <= 42.0)
            {
                bgrColor = arriBands[2].Bgr32Color;
                isMapped = true;
                return arriBands[2];
            }
            if (ire >= 52.0 && ire <= 56.0)
            {
                bgrColor = arriBands[3].Bgr32Color;
                isMapped = true;
                return arriBands[3];
            }
            if (ire >= 97.0 && ire < 99.0)
            {
                bgrColor = arriBands[4].Bgr32Color;
                isMapped = true;
                return arriBands[4];
            }
            if (ire >= 99.0 && ire <= 100.0)
            {
                bgrColor = arriBands[5].Bgr32Color;
                isMapped = true;
                return arriBands[5];
            }

            // Intermediate unmapped range
            byte lumaByte = (byte)Math.Clamp((int)Math.Round(ire * 255.0 / 100.0), 0, 255);
            bgrColor = 0xFF000000u | ((uint)lumaByte << 16) | ((uint)lumaByte << 8) | lumaByte;
            isMapped = false;
            return null;
        }
        else
        {
            // WebcamLab Custom 10-Zone Profile
            var zones = settings.CustomZones;
            if (zones == null || zones.Count != 10)
            {
                zones = FalseColorSettings.CreateDefault10Zones();
            }

            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                bool inZone;
                if (i == zones.Count - 1)
                {
                    // Top zone is inclusive of 100.0 IRE
                    inZone = ire >= z.MinIre && ire <= z.MaxIre;
                }
                else
                {
                    // Standard contiguous zone [Min, Max)
                    inZone = ire >= z.MinIre && ire < z.MaxIre;
                }

                if (inZone)
                {
                    bgrColor = z.Bgr32Color;
                    isMapped = true;
                    return z;
                }
            }

            // Fallback for floating-point margin
            var lastZone = zones[^1];
            bgrColor = lastZone.Bgr32Color;
            isMapped = true;
            return lastZone;
        }
    }

    /// <summary>
    /// Updates the internal 256-bin lookup table if settings have changed.
    /// </summary>
    public void SynchronizeLut(FalseColorSettings settings)
    {
        int zoneHash = ComputeZoneHash(settings);
        if (_cachedProfile == settings.SelectedProfile &&
            _cachedIntermediateMode == settings.ArriIntermediateMode &&
            _cachedZoneHash == zoneHash)
        {
            return;
        }

        for (int y = 0; y < 256; y++)
        {
            double ire = (y / 255.0) * 100.0;
            EvaluateZone(ire, settings, out uint color, out bool isMapped);

            _colorLut[y] = color;
            _isMappedLut[y] = isMapped;
            _lutB[y] = (byte)(color & 0xFF);
            _lutG[y] = (byte)((color >> 8) & 0xFF);
            _lutR[y] = (byte)((color >> 16) & 0xFF);
        }

        _cachedProfile = settings.SelectedProfile;
        _cachedIntermediateMode = settings.ArriIntermediateMode;
        _cachedZoneHash = zoneHash;
    }

    private static int ComputeZoneHash(FalseColorSettings settings)
    {
        int hash = 17;
        if (settings.CustomZones != null)
        {
            foreach (var z in settings.CustomZones)
            {
                hash = hash * 31 + z.MinIre.GetHashCode();
                hash = hash * 31 + z.MaxIre.GetHashCode();
                hash = hash * 31 + z.R.GetHashCode();
                hash = hash * 31 + z.G.GetHashCode();
                hash = hash * 31 + z.B.GetHashCode();
            }
        }
        return hash;
    }

    /// <summary>
    /// Renders the false-color visualization overlay.
    /// Operates entirely in presentation memory; does not mutate source or camera frames.
    /// </summary>
    /// <param name="pAnalysisSourceBgr32">Decoded source buffer analyzed for exposure (e.g. pristine camera buffer or graded buffer).</param>
    /// <param name="pDisplaySourceBgr32">Underlying image buffer used for blending and pass-through.</param>
    /// <param name="pDestBgr32">Presentation destination buffer for false-color visualization (_falseColorBuffer).</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="stride">Frame stride in bytes (width * 4).</param>
    /// <param name="settings">Active false-color configuration.</param>
    public unsafe void RenderFalseColor(
        IntPtr pAnalysisSourceBgr32,
        IntPtr pDisplaySourceBgr32,
        IntPtr pDestBgr32,
        int width,
        int height,
        int stride,
        FalseColorSettings settings)
    {
        if (pAnalysisSourceBgr32 == IntPtr.Zero || pDestBgr32 == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        SynchronizeLut(settings);

        byte* pAnalysis = (byte*)pAnalysisSourceBgr32.ToPointer();
        byte* pDisplay = (byte*)(pDisplaySourceBgr32 != IntPtr.Zero ? pDisplaySourceBgr32 : pAnalysisSourceBgr32).ToPointer();
        byte* pDest = (byte*)pDestBgr32.ToPointer();

        double opacity = Math.Clamp(settings.OverlayOpacity, 0.0, 1.0);
        bool isFullOpacity = opacity >= 0.999;
        int alpha = (int)(opacity * 256.0);
        int invAlpha = 256 - alpha;

        bool passThroughUnmapped = settings.SelectedProfile == FalseColorProfile.ArriStyleReference &&
                                   settings.ArriIntermediateMode == ArriIntermediateMode.PassThroughColor;

        fixed (uint* pLut = _colorLut)
        fixed (bool* pMapped = _isMappedLut)
        fixed (byte* pLutR = _lutR)
        fixed (byte* pLutG = _lutG)
        fixed (byte* pLutB = _lutB)
        {
            for (int y = 0; y < height; y++)
            {
                uint* pAnaRow = (uint*)(pAnalysis + y * stride);
                uint* pDispRow = (uint*)(pDisplay + y * stride);
                uint* pDstRow = (uint*)(pDest + y * stride);

                for (int x = 0; x < width; x++)
                {
                    uint anaPix = pAnaRow[x];
                    byte b = (byte)(anaPix & 0xFF);
                    byte g = (byte)((anaPix >> 8) & 0xFF);
                    byte r = (byte)((anaPix >> 16) & 0xFF);

                    // ITU-R BT.709-6 fixed-point 8-bit luma approximation:
                    // Weights: 0.2126 R + 0.7152 G + 0.0722 B -> (54 R + 183 G + 19 B + 128) >> 8
                    int yVal = (54 * r + 183 * g + 19 * b + 128) >> 8;
                    if (yVal > 255) yVal = 255;

                    bool mapped = pMapped[yVal];

                    if (passThroughUnmapped && !mapped)
                    {
                        // ARRI Pass-through mode: retain pristine underlying display pixel for intermediate ranges
                        pDstRow[x] = pDispRow[x];
                        continue;
                    }

                    if (isFullOpacity)
                    {
                        pDstRow[x] = pLut[yVal];
                    }
                    else
                    {
                        // Alpha-blend false-color output with underlying display pixel
                        uint dispPix = pDispRow[x];
                        byte dispB = (byte)(dispPix & 0xFF);
                        byte dispG = (byte)((dispPix >> 8) & 0xFF);
                        byte dispR = (byte)((dispPix >> 16) & 0xFF);

                        byte outB = (byte)((dispB * invAlpha + pLutB[yVal] * alpha) >> 8);
                        byte outG = (byte)((dispG * invAlpha + pLutG[yVal] * alpha) >> 8);
                        byte outR = (byte)((dispR * invAlpha + pLutR[yVal] * alpha) >> 8);

                        pDstRow[x] = 0xFF000000u | ((uint)outR << 16) | ((uint)outG << 8) | outB;
                    }
                }
            }
        }
    }
}
