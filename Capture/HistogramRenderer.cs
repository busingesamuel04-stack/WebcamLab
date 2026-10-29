using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WebcamLab.Capture;

public class HistogramRenderer
{
    private const int Width = 256;
    private const int Height = 100;
    private readonly WriteableBitmap _bitmap;
    private readonly uint[] _pixelBuffer = new uint[Width * Height];

    public WriteableBitmap Bitmap => _bitmap;

    public HistogramRenderer()
    {
        _bitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgr32, null);
    }

    public unsafe void Render(HistogramData data, bool showRgb)
    {
        int maxBin = Math.Max(1, data.MaxBinCount);
        int hlThreshold = data.HighlightThreshold;
        int shThreshold = data.ShadowThreshold;

        // Clear buffer with dark slate background #0F172A
        Array.Fill(_pixelBuffer, 0xFF0F172A);

        // Draw grid lines at 25%, 50%, 75%
        for (int x = 0; x < Width; x++)
        {
            _pixelBuffer[25 * Width + x] = 0xFF1E293B;
            _pixelBuffer[50 * Width + x] = 0xFF1E293B;
            _pixelBuffer[75 * Width + x] = 0xFF1E293B;
        }

        // Draw shadow and highlight threshold vertical markers
        if (shThreshold >= 0 && shThreshold < Width)
        {
            for (int y = 0; y < Height; y++) _pixelBuffer[y * Width + shThreshold] = 0xFF3B82F6; // Blue
        }
        if (hlThreshold >= 0 && hlThreshold < Width)
        {
            for (int y = 0; y < Height; y++) _pixelBuffer[y * Width + hlThreshold] = 0xFFEF4444; // Red
        }

        // Draw bins
        for (int x = 0; x < Width; x++)
        {
            int lumaH = Math.Clamp((int)((long)data.LumaBins[x] * (Height - 4) / maxBin), 0, Height - 4);
            int redH = Math.Clamp((int)((long)data.RedBins[x] * (Height - 4) / maxBin), 0, Height - 4);
            int greenH = Math.Clamp((int)((long)data.GreenBins[x] * (Height - 4) / maxBin), 0, Height - 4);
            int blueH = Math.Clamp((int)((long)data.BlueBins[x] * (Height - 4) / maxBin), 0, Height - 4);

            int startY = Height - 1;

            if (showRgb)
            {
                // RGB mode: combine channels
                for (int y = 0; y < Height; y++)
                {
                    int distFromBottom = Height - 1 - y;
                    uint color = 0;
                    if (distFromBottom <= redH) color |= 0xFFFF3333; // Red
                    if (distFromBottom <= greenH) color |= 0xFF33FF33; // Green
                    if (distFromBottom <= blueH) color |= 0xFF3388FF; // Blue

                    if (color != 0)
                    {
                        _pixelBuffer[y * Width + x] = 0xFF000000 | color;
                    }
                }
            }
            else
            {
                // Luminance Mode: Clean silver/cyan gradient
                for (int h = 0; h < lumaH; h++)
                {
                    int y = startY - h;
                    if (y >= 0 && y < Height)
                    {
                        _pixelBuffer[y * Width + x] = 0xFF38BDF8; // Light Sky Blue
                    }
                }
            }
        }

        _bitmap.Lock();
        fixed (uint* pSrc = _pixelBuffer)
        {
            Buffer.MemoryCopy(
                pSrc,
                (void*)_bitmap.BackBuffer,
                _bitmap.BackBufferStride * Height,
                Width * Height * 4);
        }
        _bitmap.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
        _bitmap.Unlock();
    }
}
