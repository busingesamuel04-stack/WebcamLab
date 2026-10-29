using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WebcamLab.Capture;

public enum CubeLutType
{
    None,
    Lut1D,
    Lut3D
}

/// <summary>
/// High-performance parser and evaluator for standard Adobe / DaVinci Resolve .cube LUT files.
/// Supports both 1D and 3D LUTs with trilinear interpolation and arbitrary domain bounds.
/// </summary>
public class CubeLut
{
    public string Title { get; private set; } = string.Empty;
    public CubeLutType Type { get; private set; } = CubeLutType.None;
    public int Size { get; private set; }
    public float[] DomainMin { get; } = [0f, 0f, 0f];
    public float[] DomainMax { get; } = [1f, 1f, 1f];

    // Flat table data: length is Size * 3 for 1D, or Size^3 * 3 for 3D
    private float[] _data = [];

    public bool IsLoaded => Type != CubeLutType.None && _data.Length > 0;

    /// <summary>
    /// Parses a .cube file from disk with strict format and entry count validation.
    /// </summary>
    public static CubeLut LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"LUT file not found: {filePath}");
        }

        using var stream = File.OpenRead(filePath);
        return LoadFromStream(stream, Path.GetFileName(filePath));
    }

    /// <summary>
    /// Parses a .cube format string with strict validation.
    /// </summary>
    public static CubeLut Parse(string text, string sourceName = "string")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return LoadFromStream(stream, sourceName);
    }

    /// <summary>
    /// Parses a .cube stream with strict validation.
    /// </summary>
    public static CubeLut LoadFromStream(Stream stream, string sourceName = "stream")
    {
        var lut = new CubeLut();
        using var reader = new StreamReader(stream);

        string? line;
        int lineNumber = 0;
        var dataValues = new List<float>();

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            string trimmed = line.Trim();

            // Skip empty lines and comment lines
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Keyword parsing
            if (trimmed.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            {
                lut.Title = trimmed[5..].Trim().Trim('"');
                continue;
            }

            if (trimmed.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                string valStr = trimmed[11..].Trim();
                if (!int.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) || size < 2 || size > 256)
                {
                    throw new FormatException($"[{sourceName}:{lineNumber}] Invalid LUT_3D_SIZE '{valStr}'. Expected integer between 2 and 256.");
                }
                lut.Type = CubeLutType.Lut3D;
                lut.Size = size;
                continue;
            }

            if (trimmed.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                string valStr = trimmed[11..].Trim();
                if (!int.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) || size < 2 || size > 65536)
                {
                    throw new FormatException($"[{sourceName}:{lineNumber}] Invalid LUT_1D_SIZE '{valStr}'. Expected integer between 2 and 65536.");
                }
                lut.Type = CubeLutType.Lut1D;
                lut.Size = size;
                continue;
            }

            if (trimmed.StartsWith("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase))
            {
                ParseFloatTriplet(trimmed[10..].Trim(), lut.DomainMin, sourceName, lineNumber, "DOMAIN_MIN");
                continue;
            }

            if (trimmed.StartsWith("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                ParseFloatTriplet(trimmed[10..].Trim(), lut.DomainMax, sourceName, lineNumber, "DOMAIN_MAX");
                continue;
            }

            // Otherwise, line must be an RGB table entry
            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                {
                    dataValues.Add(r);
                    dataValues.Add(g);
                    dataValues.Add(b);
                }
                else
                {
                    throw new FormatException($"[{sourceName}:{lineNumber}] Malformed numeric data in RGB entry: '{trimmed}'.");
                }
            }
            else
            {
                throw new FormatException($"[{sourceName}:{lineNumber}] Unexpected token count ({parts.Length}). Expected 3 RGB values: '{trimmed}'.");
            }
        }

        if (lut.Type == CubeLutType.None)
        {
            throw new FormatException($"[{sourceName}] Missing required LUT_3D_SIZE or LUT_1D_SIZE header.");
        }

        int expectedTriplets = lut.Type == CubeLutType.Lut3D
            ? lut.Size * lut.Size * lut.Size
            : lut.Size;

        int actualTriplets = dataValues.Count / 3;
        if (actualTriplets != expectedTriplets)
        {
            throw new FormatException($"[{sourceName}] Entry count mismatch. Header declared size {lut.Size} (expected {expectedTriplets} entries), but file contained {actualTriplets} entries.");
        }

        lut._data = dataValues.ToArray();
        return lut;
    }

    /// <summary>
    /// Creates a calibrated warm film profile 3D LUT (17x17x17).
    /// </summary>
    public static CubeLut CreateFilmWarmth3D(int size = 17)
    {
        var lut = new CubeLut
        {
            Type = CubeLutType.Lut3D,
            Size = size,
            Title = "Film Warmth 17x17x17",
            _data = new float[size * size * size * 3]
        };

        float step = 1.0f / (size - 1);
        int idx = 0;
        for (int b = 0; b < size; b++)
        {
            float bVal = b * step;
            for (int g = 0; g < size; g++)
            {
                float gVal = g * step;
                for (int r = 0; r < size; r++)
                {
                    float rVal = r * step;
                    float outR = Math.Clamp(rVal * 1.05f + 0.02f * (1.0f - rVal), 0.0f, 1.0f);
                    float outG = Math.Clamp(gVal * 1.01f + 0.005f * (1.0f - gVal), 0.0f, 1.0f);
                    float outB = Math.Clamp(bVal * 0.94f, 0.0f, 1.0f);
                    lut._data[idx++] = outR;
                    lut._data[idx++] = outG;
                    lut._data[idx++] = outB;
                }
            }
        }
        return lut;
    }

    /// <summary>
    /// Creates a calibrated cool steel cinema profile 3D LUT (17x17x17).
    /// </summary>
    public static CubeLut CreateCoolSteel3D(int size = 17)
    {
        var lut = new CubeLut
        {
            Type = CubeLutType.Lut3D,
            Size = size,
            Title = "Cool Steel 17x17x17",
            _data = new float[size * size * size * 3]
        };

        float step = 1.0f / (size - 1);
        int idx = 0;
        for (int b = 0; b < size; b++)
        {
            float bVal = b * step;
            for (int g = 0; g < size; g++)
            {
                float gVal = g * step;
                for (int r = 0; r < size; r++)
                {
                    float rVal = r * step;
                    float outR = Math.Clamp(rVal * 0.94f, 0.0f, 1.0f);
                    float outG = Math.Clamp(gVal * 1.01f, 0.0f, 1.0f);
                    float outB = Math.Clamp(bVal * 1.08f + 0.02f * (1.0f - bVal), 0.0f, 1.0f);
                    lut._data[idx++] = outR;
                    lut._data[idx++] = outG;
                    lut._data[idx++] = outB;
                }
            }
        }
        return lut;
    }

    /// <summary>
    /// Creates a calibrated high-contrast monochrome profile 3D LUT (17x17x17).
    /// </summary>
    public static CubeLut CreateBlackAndWhite3D(int size = 17)
    {
        var lut = new CubeLut
        {
            Type = CubeLutType.Lut3D,
            Size = size,
            Title = "Black and White 17x17x17",
            _data = new float[size * size * size * 3]
        };

        float step = 1.0f / (size - 1);
        int idx = 0;
        for (int b = 0; b < size; b++)
        {
            float bVal = b * step;
            for (int g = 0; g < size; g++)
            {
                float gVal = g * step;
                for (int r = 0; r < size; r++)
                {
                    float rVal = r * step;
                    // Rec.709 luma
                    float y = 0.2126f * rVal + 0.7152f * gVal + 0.0722f * bVal;
                    // S-curve contrast on luma
                    float yC = Math.Clamp(y * y * (3.0f - 2.0f * y), 0.0f, 1.0f);
                    lut._data[idx++] = yC;
                    lut._data[idx++] = yC;
                    lut._data[idx++] = yC;
                }
            }
        }
        return lut;
    }

    /// <summary>
    /// Saves the current LUT to a standard .cube text file.
    /// </summary>
    public void SaveToFile(string filePath)
    {
        if (!IsLoaded) throw new InvalidOperationException("Cannot save an uninitialized LUT.");

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        writer.WriteLine("# WebcamLab Standard 3D LUT");
        writer.WriteLine($"TITLE \"{Title}\"");

        if (Type == CubeLutType.Lut3D)
        {
            writer.WriteLine($"LUT_3D_SIZE {Size}");
        }
        else
        {
            writer.WriteLine($"LUT_1D_SIZE {Size}");
        }

        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "DOMAIN_MIN {0:F6} {1:F6} {2:F6}", DomainMin[0], DomainMin[1], DomainMin[2]));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "DOMAIN_MAX {0:F6} {1:F6} {2:F6}", DomainMax[0], DomainMax[1], DomainMax[2]));
        writer.WriteLine();

        int totalTriplets = (Type == CubeLutType.Lut3D) ? Size * Size * Size : Size;
        for (int i = 0; i < totalTriplets; i++)
        {
            int idx = i * 3;
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F6} {1:F6} {2:F6}", _data[idx], _data[idx + 1], _data[idx + 2]));
        }
    }

    /// <summary>
    /// Ensures that standard starter .cube LUT files are created in the specified directory.
    /// </summary>
    public static void EnsureStandardLuts(string directory)
    {
        Directory.CreateDirectory(directory);

        string idPath = Path.Combine(directory, "identity.cube");
        if (!File.Exists(idPath))
        {
            CreateIdentity3D(17, "Identity 17x17x17").SaveToFile(idPath);
        }

        string warmPath = Path.Combine(directory, "film_warmth.cube");
        if (!File.Exists(warmPath))
        {
            CreateFilmWarmth3D(17).SaveToFile(warmPath);
        }

        string coolPath = Path.Combine(directory, "cool_steel.cube");
        if (!File.Exists(coolPath))
        {
            CreateCoolSteel3D(17).SaveToFile(coolPath);
        }

        string bwPath = Path.Combine(directory, "black_and_white.cube");
        if (!File.Exists(bwPath))
        {
            CreateBlackAndWhite3D(17).SaveToFile(bwPath);
        }
    }

    private static void ParseFloatTriplet(string text, float[] dest, string sourceName, int line, string keyword)
    {
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out dest[0]) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out dest[1]) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out dest[2]))
        {
            throw new FormatException($"[{sourceName}:{line}] Invalid {keyword} format. Expected 3 floats: '{text}'.");
        }
    }

    /// <summary>
    /// Creates a mathematical 3D Identity LUT of the specified size (default 17x17x17).
    /// </summary>
    public static CubeLut CreateIdentity3D(int size = 17, string title = "Identity 3D")
    {
        var lut = new CubeLut
        {
            Type = CubeLutType.Lut3D,
            Size = size,
            Title = title,
            _data = new float[size * size * size * 3]
        };

        float step = 1.0f / (size - 1);
        int idx = 0;

        for (int b = 0; b < size; b++)
        {
            float bVal = b * step;
            for (int g = 0; g < size; g++)
            {
                float gVal = g * step;
                for (int r = 0; r < size; r++)
                {
                    float rVal = r * step;
                    lut._data[idx++] = rVal;
                    lut._data[idx++] = gVal;
                    lut._data[idx++] = bVal;
                }
            }
        }

        return lut;
    }

    /// <summary>
    /// Evaluates a single RGB byte triplet through the LUT and returns the transformed RGB bytes.
    /// </summary>
    public (byte R, byte G, byte B) Sample(byte inR, byte inG, byte inB)
    {
        if (!IsLoaded) return (inR, inG, inB);

        // 1. Normalize and map to domain
        float normR = inR / 255.0f;
        float normG = inG / 255.0f;
        float normB = inB / 255.0f;

        float domRangeR = DomainMax[0] - DomainMin[0];
        float domRangeG = DomainMax[1] - DomainMin[1];
        float domRangeB = DomainMax[2] - DomainMin[2];

        float rIn = domRangeR > 0f ? Math.Clamp((normR - DomainMin[0]) / domRangeR, 0f, 1f) : normR;
        float gIn = domRangeG > 0f ? Math.Clamp((normG - DomainMin[1]) / domRangeG, 0f, 1f) : normG;
        float bIn = domRangeB > 0f ? Math.Clamp((normB - DomainMin[2]) / domRangeB, 0f, 1f) : normB;

        if (Type == CubeLutType.Lut3D)
        {
            return Sample3D(rIn, gIn, bIn);
        }
        else
        {
            return Sample1D(rIn, gIn, bIn);
        }
    }

    private (byte R, byte G, byte B) Sample1D(float rIn, float gIn, float bIn)
    {
        int maxIdx = Size - 1;
        float uR = rIn * maxIdx;
        float uG = gIn * maxIdx;
        float uB = bIn * maxIdx;

        int iR0 = (int)uR;
        int iR1 = Math.Min(iR0 + 1, maxIdx);
        float dR = uR - iR0;

        int iG0 = (int)uG;
        int iG1 = Math.Min(iG0 + 1, maxIdx);
        float dG = uG - iG0;

        int iB0 = (int)uB;
        int iB1 = Math.Min(iB0 + 1, maxIdx);
        float dB = uB - iB0;

        float outR = _data[iR0 * 3 + 0] * (1f - dR) + _data[iR1 * 3 + 0] * dR;
        float outG = _data[iG0 * 3 + 1] * (1f - dG) + _data[iG1 * 3 + 1] * dG;
        float outB = _data[iB0 * 3 + 2] * (1f - dB) + _data[iB1 * 3 + 2] * dB;

        return (
            (byte)Math.Clamp((int)(outR * 255.0f + 0.5f), 0, 255),
            (byte)Math.Clamp((int)(outG * 255.0f + 0.5f), 0, 255),
            (byte)Math.Clamp((int)(outB * 255.0f + 0.5f), 0, 255)
        );
    }

    private (byte R, byte G, byte B) Sample3D(float rIn, float gIn, float bIn)
    {
        int maxIdx = Size - 1;
        float u = rIn * maxIdx;
        float v = gIn * maxIdx;
        float w = bIn * maxIdx;

        int i0 = (int)u;
        int i1 = Math.Min(i0 + 1, maxIdx);
        float du = u - i0;

        int j0 = (int)v;
        int j1 = Math.Min(j0 + 1, maxIdx);
        float dv = v - j0;

        int k0 = (int)w;
        int k1 = Math.Min(k0 + 1, maxIdx);
        float dw = w - k0;

        int strideG = Size;
        int strideB = Size * Size;

        // 8 corner offsets in float array (each entry is 3 floats)
        int c000 = (i0 + j0 * strideG + k0 * strideB) * 3;
        int c100 = (i1 + j0 * strideG + k0 * strideB) * 3;
        int c010 = (i0 + j1 * strideG + k0 * strideB) * 3;
        int c110 = (i1 + j1 * strideG + k0 * strideB) * 3;
        int c001 = (i0 + j0 * strideG + k1 * strideB) * 3;
        int c101 = (i1 + j0 * strideG + k1 * strideB) * 3;
        int c011 = (i0 + j1 * strideG + k1 * strideB) * 3;
        int c111 = (i1 + j1 * strideG + k1 * strideB) * 3;

        // Trilinear interpolation for Red
        float r00 = _data[c000 + 0] * (1f - du) + _data[c100 + 0] * du;
        float r10 = _data[c010 + 0] * (1f - du) + _data[c110 + 0] * du;
        float r01 = _data[c001 + 0] * (1f - du) + _data[c101 + 0] * du;
        float r11 = _data[c011 + 0] * (1f - du) + _data[c111 + 0] * du;
        float r0 = r00 * (1f - dv) + r10 * dv;
        float r1 = r01 * (1f - dv) + r11 * dv;
        float finalR = r0 * (1f - dw) + r1 * dw;

        // Trilinear interpolation for Green
        float g00 = _data[c000 + 1] * (1f - du) + _data[c100 + 1] * du;
        float g10 = _data[c010 + 1] * (1f - du) + _data[c110 + 1] * du;
        float g01 = _data[c001 + 1] * (1f - du) + _data[c101 + 1] * du;
        float g11 = _data[c011 + 1] * (1f - du) + _data[c111 + 1] * du;
        float g0 = g00 * (1f - dv) + g10 * dv;
        float g1 = g01 * (1f - dv) + g11 * dv;
        float finalG = g0 * (1f - dw) + g1 * dw;

        // Trilinear interpolation for Blue
        float b00 = _data[c000 + 2] * (1f - du) + _data[c100 + 2] * du;
        float b10 = _data[c010 + 2] * (1f - du) + _data[c110 + 2] * du;
        float b01 = _data[c001 + 2] * (1f - du) + _data[c101 + 2] * du;
        float b11 = _data[c011 + 2] * (1f - du) + _data[c111 + 2] * du;
        float b0 = b00 * (1f - dv) + b10 * dv;
        float b1 = b01 * (1f - dv) + b11 * dv;
        float finalB = b0 * (1f - dw) + b1 * dw;

        return (
            (byte)Math.Clamp((int)(finalR * 255.0f + 0.5f), 0, 255),
            (byte)Math.Clamp((int)(finalG * 255.0f + 0.5f), 0, 255),
            (byte)Math.Clamp((int)(finalB * 255.0f + 0.5f), 0, 255)
        );
    }

    /// <summary>
    /// High-performance bulk apply across an entire unmanaged BGR32 frame buffer.
    /// </summary>
    public unsafe void ApplyInPlace(byte* pBgrRow, int width, int height, int stride)
    {
        if (!IsLoaded) return;

        fixed (float* pTable = _data)
        {
            int size = Size;
            int maxIdx = size - 1;
            int strideG = size;
            int strideB = size * size;
            float maxIdxF = (float)maxIdx;
            float inv255 = 1.0f / 255.0f;

            if (Type == CubeLutType.Lut3D)
            {
                for (int y = 0; y < height; y++)
                {
                    byte* pPix = pBgrRow + y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        byte inB = pPix[0];
                        byte inG = pPix[1];
                        byte inR = pPix[2];

                        float u = (inR * inv255) * maxIdxF;
                        float v = (inG * inv255) * maxIdxF;
                        float w = (inB * inv255) * maxIdxF;

                        int i0 = (int)u;
                        int i1 = i0 < maxIdx ? i0 + 1 : maxIdx;
                        float du = u - i0;

                        int j0 = (int)v;
                        int j1 = j0 < maxIdx ? j0 + 1 : maxIdx;
                        float dv = v - j0;

                        int k0 = (int)w;
                        int k1 = k0 < maxIdx ? k0 + 1 : maxIdx;
                        float dw = w - k0;

                        int c000 = (i0 + j0 * strideG + k0 * strideB) * 3;
                        int c100 = (i1 + j0 * strideG + k0 * strideB) * 3;
                        int c010 = (i0 + j1 * strideG + k0 * strideB) * 3;
                        int c110 = (i1 + j1 * strideG + k0 * strideB) * 3;
                        int c001 = (i0 + j0 * strideG + k1 * strideB) * 3;
                        int c101 = (i1 + j0 * strideG + k1 * strideB) * 3;
                        int c011 = (i0 + j1 * strideG + k1 * strideB) * 3;
                        int c111 = (i1 + j1 * strideG + k1 * strideB) * 3;

                        // R
                        float r00 = pTable[c000 + 0] * (1f - du) + pTable[c100 + 0] * du;
                        float r10 = pTable[c010 + 0] * (1f - du) + pTable[c110 + 0] * du;
                        float r01 = pTable[c001 + 0] * (1f - du) + pTable[c101 + 0] * du;
                        float r11 = pTable[c011 + 0] * (1f - du) + pTable[c111 + 0] * du;
                        float r0 = r00 * (1f - dv) + r10 * dv;
                        float r1 = r01 * (1f - dv) + r11 * dv;
                        float fR = r0 * (1f - dw) + r1 * dw;

                        // G
                        float g00 = pTable[c000 + 1] * (1f - du) + pTable[c100 + 1] * du;
                        float g10 = pTable[c010 + 1] * (1f - du) + pTable[c110 + 1] * du;
                        float g01 = pTable[c001 + 1] * (1f - du) + pTable[c101 + 1] * du;
                        float g11 = pTable[c011 + 1] * (1f - du) + pTable[c111 + 1] * du;
                        float g0 = g00 * (1f - dv) + g10 * dv;
                        float g1 = g01 * (1f - dv) + g11 * dv;
                        float fG = g0 * (1f - dw) + g1 * dw;

                        // B
                        float b00 = pTable[c000 + 2] * (1f - du) + pTable[c100 + 2] * du;
                        float b10 = pTable[c010 + 2] * (1f - du) + pTable[c110 + 2] * du;
                        float b01 = pTable[c001 + 2] * (1f - du) + pTable[c101 + 2] * du;
                        float b11 = pTable[c011 + 2] * (1f - du) + pTable[c111 + 2] * du;
                        float b0 = b00 * (1f - dv) + b10 * dv;
                        float b1 = b01 * (1f - dv) + b11 * dv;
                        float fB = b0 * (1f - dw) + b1 * dw;

                        int outB = (int)(fB * 255.0f + 0.5f);
                        int outG = (int)(fG * 255.0f + 0.5f);
                        int outR = (int)(fR * 255.0f + 0.5f);

                        pPix[0] = (byte)(outB < 0 ? 0 : (outB > 255 ? 255 : outB));
                        pPix[1] = (byte)(outG < 0 ? 0 : (outG > 255 ? 255 : outG));
                        pPix[2] = (byte)(outR < 0 ? 0 : (outR > 255 ? 255 : outR));
                        // Alpha remains 255

                        pPix += 4;
                    }
                }
            }
            else if (Type == CubeLutType.Lut1D)
            {
                for (int y = 0; y < height; y++)
                {
                    byte* pPix = pBgrRow + y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        float u = (pPix[2] * inv255) * maxIdxF;
                        float v = (pPix[1] * inv255) * maxIdxF;
                        float w = (pPix[0] * inv255) * maxIdxF;

                        int i0 = (int)u; int i1 = i0 < maxIdx ? i0 + 1 : maxIdx; float du = u - i0;
                        int j0 = (int)v; int j1 = j0 < maxIdx ? j0 + 1 : maxIdx; float dv = v - j0;
                        int k0 = (int)w; int k1 = k0 < maxIdx ? k0 + 1 : maxIdx; float dw = w - k0;

                        float fR = pTable[i0 * 3 + 0] * (1f - du) + pTable[i1 * 3 + 0] * du;
                        float fG = pTable[j0 * 3 + 1] * (1f - dv) + pTable[j1 * 3 + 1] * dv;
                        float fB = pTable[k0 * 3 + 2] * (1f - dw) + pTable[k1 * 3 + 2] * dw;

                        int outB = (int)(fB * 255.0f + 0.5f);
                        int outG = (int)(fG * 255.0f + 0.5f);
                        int outR = (int)(fR * 255.0f + 0.5f);

                        pPix[0] = (byte)(outB < 0 ? 0 : (outB > 255 ? 255 : outB));
                        pPix[1] = (byte)(outG < 0 ? 0 : (outG > 255 ? 255 : outG));
                        pPix[2] = (byte)(outR < 0 ? 0 : (outR > 255 ? 255 : outR));
                        pPix += 4;
                    }
                }
            }
        }
    }
}
