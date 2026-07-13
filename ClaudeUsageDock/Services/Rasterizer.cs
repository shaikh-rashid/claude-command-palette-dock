using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClaudeUsageDock.Services;

/// <summary>
/// Shared pixel primitives for the in-process PNG renderers (the heatmap and the
/// usage bars): plain BGRA-buffer fills, supersample box-filtering, and encoding
/// through the OS PNG encoder — so no drawing library is pulled in and the two
/// renderers can't drift apart on the basics.
/// </summary>
internal static class Rasterizer
{
    /// <summary>Renderers draw at this multiple and box-filter down, which anti-aliases rounded corners.</summary>
    public const int SuperSample = 3;

    public static void FillRect(byte[] canvas, int w, int left, int top, int width, int height, byte[] color)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var i = (y * w + x) * 4;
                canvas[i] = color[0];
                canvas[i + 1] = color[1];
                canvas[i + 2] = color[2];
                canvas[i + 3] = 0xFF;
            }
        }
    }

    public static void FillRoundedRect(byte[] canvas, int w, int left, int top, int width, int height, int radius, byte[] color)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                // Corner test: within a corner square, the pixel must also fall
                // inside the quarter-circle around that corner's center.
                var dx = Math.Max(Math.Max(left + radius - x, x - (left + width - 1 - radius)), 0);
                var dy = Math.Max(Math.Max(top + radius - y, y - (top + height - 1 - radius)), 0);
                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }

                var i = (y * w + x) * 4;
                canvas[i] = color[0];
                canvas[i + 1] = color[1];
                canvas[i + 2] = color[2];
                canvas[i + 3] = 0xFF;
            }
        }
    }

    /// <summary>
    /// Box-filters the supersampled canvas down to the output size, averaging all
    /// four channels. Untouched canvas pixels are transparent black, so averaged
    /// values stay premultiplied-consistent — encode such output with
    /// <see cref="BitmapAlphaMode.Premultiplied"/> for clean soft edges.
    /// </summary>
    public static byte[] Downsample(byte[] canvas, int canvasWidth, int outWidth, int outHeight)
    {
        var pixels = new byte[outWidth * outHeight * 4];
        for (var fy = 0; fy < outHeight; fy++)
        {
            for (var fx = 0; fx < outWidth; fx++)
            {
                int b = 0, g = 0, r = 0, a = 0;
                for (var sy = 0; sy < SuperSample; sy++)
                {
                    for (var sx = 0; sx < SuperSample; sx++)
                    {
                        var i = ((fy * SuperSample + sy) * canvasWidth + fx * SuperSample + sx) * 4;
                        b += canvas[i];
                        g += canvas[i + 1];
                        r += canvas[i + 2];
                        a += canvas[i + 3];
                    }
                }

                const int Samples = SuperSample * SuperSample;
                var o = (fy * outWidth + fx) * 4;
                pixels[o] = (byte)(b / Samples);
                pixels[o + 1] = (byte)(g / Samples);
                pixels[o + 2] = (byte)(r / Samples);
                pixels[o + 3] = (byte)(a / Samples);
            }
        }

        return pixels;
    }

    public static byte[] EncodePng(byte[] bgraPixels, int width, int height, BitmapAlphaMode alphaMode)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask().GetAwaiter().GetResult();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, alphaMode, (uint)width, (uint)height, 96, 96, bgraPixels);
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

        var bytes = new byte[(int)stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        reader.LoadAsync((uint)bytes.Length).AsTask().GetAwaiter().GetResult();
        reader.ReadBytes(bytes);
        return bytes;
    }
}
