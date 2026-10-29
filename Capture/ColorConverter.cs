using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WebcamLab.Capture;

public static class ColorConverter
{
    /// <summary>
    /// Fast integer fixed-point YUY2 (4:2:2) to BGR32 conversion.
    /// When isStudioRange is true, maps ITU-R BT.709 studio video range [16..235] to full sRGB [0..255].
    /// When isStudioRange is false, assumes full-range YCbCr [0..255].
    /// </summary>
    public static unsafe void ConvertYuy2ToBgr32(IntPtr srcPtr, IntPtr dstPtr, int width, int height, bool isStudioRange = false)
    {
        byte* src = (byte*)srcPtr.ToPointer();
        byte* dst = (byte*)dstPtr.ToPointer();
        int totalPairs = (width * height) / 2;

        if (isStudioRange)
        {
            // ITU-R BT.709 Studio [16..235] to Full [0..255] expansion:
            // C = Y - 16
            // R = clamp((298 * C + 409 * V + 128) >> 8)
            // G = clamp((298 * C - 100 * U - 208 * V + 128) >> 8)
            // B = clamp((298 * C + 516 * U + 128) >> 8)
            for (int i = 0; i < totalPairs; i++)
            {
                int c0 = src[0] - 16;
                int u  = src[1] - 128;
                int c1 = src[2] - 16;
                int v  = src[3] - 128;
                src += 4;

                // Pixel 0
                int r0 = (298 * c0 + 409 * v + 128) >> 8;
                int g0 = (298 * c0 - 100 * u - 208 * v + 128) >> 8;
                int b0 = (298 * c0 + 516 * u + 128) >> 8;

                dst[0] = (byte)(b0 < 0 ? 0 : (b0 > 255 ? 255 : b0));
                dst[1] = (byte)(g0 < 0 ? 0 : (g0 > 255 ? 255 : g0));
                dst[2] = (byte)(r0 < 0 ? 0 : (r0 > 255 ? 255 : r0));
                dst[3] = 255;

                // Pixel 1
                int r1 = (298 * c1 + 409 * v + 128) >> 8;
                int g1 = (298 * c1 - 100 * u - 208 * v + 128) >> 8;
                int b1 = (298 * c1 + 516 * u + 128) >> 8;

                dst[4] = (byte)(b1 < 0 ? 0 : (b1 > 255 ? 255 : b1));
                dst[5] = (byte)(g1 < 0 ? 0 : (g1 > 255 ? 255 : g1));
                dst[6] = (byte)(r1 < 0 ? 0 : (r1 > 255 ? 255 : r1));
                dst[7] = 255;

                dst += 8;
            }
        }
        else
        {
            // Full-range JFIF/sRGB formula [0..255]
            for (int i = 0; i < totalPairs; i++)
            {
                int y0 = src[0];
                int u  = src[1] - 128;
                int y1 = src[2];
                int v  = src[3] - 128;
                src += 4;

                // Pixel 0
                int r0 = y0 + ((359 * v) >> 8);
                int g0 = y0 - ((88 * u + 183 * v) >> 8);
                int b0 = y0 + ((454 * u) >> 8);

                dst[0] = (byte)(b0 < 0 ? 0 : (b0 > 255 ? 255 : b0));
                dst[1] = (byte)(g0 < 0 ? 0 : (g0 > 255 ? 255 : g0));
                dst[2] = (byte)(r0 < 0 ? 0 : (r0 > 255 ? 255 : r0));
                dst[3] = 255;

                // Pixel 1
                int r1 = y1 + ((359 * v) >> 8);
                int g1 = y1 - ((88 * u + 183 * v) >> 8);
                int b1 = y1 + ((454 * u) >> 8);

                dst[4] = (byte)(b1 < 0 ? 0 : (b1 > 255 ? 255 : b1));
                dst[5] = (byte)(g1 < 0 ? 0 : (g1 > 255 ? 255 : g1));
                dst[6] = (byte)(r1 < 0 ? 0 : (r1 > 255 ? 255 : r1));
                dst[7] = 255;

                dst += 8;
            }
        }
    }

    /// <summary>
    /// Fast NV12 to BGR32 conversion.
    /// When isStudioRange is true, maps ITU-R BT.709 studio video range [16..235] to full sRGB [0..255].
    /// </summary>
    public static unsafe void ConvertNv12ToBgr32(IntPtr srcPtr, IntPtr dstPtr, int width, int height, bool isStudioRange = false)
    {
        byte* yPlane = (byte*)srcPtr.ToPointer();
        byte* uvPlane = yPlane + (width * height);
        byte* dst = (byte*)dstPtr.ToPointer();

        for (int y = 0; y < height; y++)
        {
            int uvRow = (y / 2) * width;
            int yRow = y * width;

            for (int x = 0; x < width; x++)
            {
                int uvIdx = uvRow + (x & ~1);
                int u = uvPlane[uvIdx] - 128;
                int v = uvPlane[uvIdx + 1] - 128;
                int r, g, b;

                if (isStudioRange)
                {
                    int c = yPlane[yRow + x] - 16;
                    r = (298 * c + 409 * v + 128) >> 8;
                    g = (298 * c - 100 * u - 208 * v + 128) >> 8;
                    b = (298 * c + 516 * u + 128) >> 8;
                }
                else
                {
                    int yVal = yPlane[yRow + x];
                    r = yVal + ((359 * v) >> 8);
                    g = yVal - ((88 * u + 183 * v) >> 8);
                    b = yVal + ((454 * u) >> 8);
                }

                int dstIdx = (yRow + x) * 4;
                dst[dstIdx + 0] = (byte)(b < 0 ? 0 : (b > 255 ? 255 : b));
                dst[dstIdx + 1] = (byte)(g < 0 ? 0 : (g > 255 ? 255 : g));
                dst[dstIdx + 2] = (byte)(r < 0 ? 0 : (r > 255 ? 255 : r));
                dst[dstIdx + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Decodes an MJPEG frame (raw JPEG byte buffer) into an unmanaged BGR32 target buffer.
    /// </summary>
    public static unsafe void DecodeMjpegToBgr32(IntPtr jpegDataPtr, uint length, IntPtr dstPtr, int width, int height)
    {
        using var stream = new UnmanagedMemoryStream((byte*)jpegDataPtr.ToPointer(), length);
        var decoder = new JpegBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count > 0)
        {
            var frame = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
            converted.CopyPixels(new System.Windows.Int32Rect(0, 0, width, height), dstPtr, width * height * 4, width * 4);
        }
    }
}
