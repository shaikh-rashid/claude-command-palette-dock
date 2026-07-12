using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClaudeUsageDock.Services;

/// <summary>
/// Rasterizes the weekly usage trend into a small PNG area chart: dark navy panel,
/// teal fill, cyan line, hairline border. Pixel work happens on a plain BGRA buffer
/// and the OS PNG encoder does the rest, so no drawing library is pulled in. The
/// panel bakes in its own dark surface so it reads the same on light and dark themes.
/// </summary>
internal static class TrendChartRenderer
{
    /// <summary>Drawn at 3x and box-filtered down, which anti-aliases the line and fill edge.</summary>
    private const int SuperSample = 3;

    /// <summary>Headroom kept above the highest point, as a fraction of panel height.</summary>
    private const double TopPadding = 0.12;

    // Panel palette, stored B,G,R to match the buffer layout.
    private static readonly byte[] Surface = [0x2D, 0x22, 0x17]; // #17222D
    private static readonly byte[] Fill = [0x55, 0x47, 0x1E];    // #1E4755
    private static readonly byte[] Stroke = [0xE8, 0xD0, 0x62];  // #62D0E8
    private static readonly byte[] Border = [0x7E, 0x77, 0x71];  // #71777E

    /// <summary>
    /// Renders the chart to PNG bytes. Points are normalized — X runs 0→1 left to
    /// right (ascending), Y runs 0→1 from the bottom baseline to full panel height
    /// (the top-padding headroom is applied here). The output bitmap is
    /// <paramref name="displayWidth"/> × <paramref name="displayHeight"/> times
    /// <paramref name="pixelRatio"/>, so a ratio of 2 stays crisp on high-DPI
    /// screens when shown at the display size. Returns null when there are fewer
    /// than two points to connect.
    /// </summary>
    public static byte[]? Render(IReadOnlyList<(double X, double Y)> points, int displayWidth, int displayHeight, int pixelRatio = 2)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var outWidth = displayWidth * pixelRatio;
        var outHeight = displayHeight * pixelRatio;
        var w = outWidth * SuperSample;
        var h = outHeight * SuperSample;

        var canvas = new byte[w * h * 4];
        FillRect(canvas, w, 0, 0, w, h, Surface);

        // Piecewise-linear y (in canvas pixels, from the top) for every column.
        var plotTop = TopPadding * h;
        var columnY = new double[w];
        var segment = 0;
        for (var x = 0; x < w; x++)
        {
            var xn = (double)x / (w - 1);
            while (segment < points.Count - 2 && points[segment + 1].X < xn)
            {
                segment++;
            }

            var (x0, y0) = points[segment];
            var (x1, y1) = points[segment + 1];
            var t = x1 > x0 ? Math.Clamp((xn - x0) / (x1 - x0), 0, 1) : 0;
            var yn = Math.Clamp(y0 + (y1 - y0) * t, 0, 1);
            columnY[x] = plotTop + (1 - yn) * (h - 1 - plotTop);
        }

        // Area fill: everything from the curve down to the baseline.
        for (var x = 0; x < w; x++)
        {
            FillColumn(canvas, w, h, x, columnY[x], h - 1, Fill);
        }

        // Stroke: connect each column to its neighbor vertically so steep slopes
        // stay solid, then pad by the stroke radius for thickness.
        var strokeRadius = 1.1 * pixelRatio * SuperSample;
        for (var x = 0; x < w; x++)
        {
            var previous = columnY[Math.Max(x - 1, 0)];
            var current = columnY[x];
            FillColumn(canvas, w, h, x, Math.Min(previous, current) - strokeRadius, Math.Max(previous, current) + strokeRadius, Stroke);
        }

        var pixels = Downsample(canvas, w, outWidth, outHeight);
        DrawBorder(pixels, outWidth, outHeight, pixelRatio);
        return EncodePng(pixels, outWidth, outHeight);
    }

    private static void FillColumn(byte[] canvas, int w, int h, int x, double yTop, double yBottom, byte[] color)
    {
        var from = Math.Clamp((int)Math.Round(yTop), 0, h - 1);
        var to = Math.Clamp((int)Math.Round(yBottom), 0, h - 1);
        for (var y = from; y <= to; y++)
        {
            var i = (y * w + x) * 4;
            canvas[i] = color[0];
            canvas[i + 1] = color[1];
            canvas[i + 2] = color[2];
            canvas[i + 3] = 0xFF;
        }
    }

    private static void FillRect(byte[] canvas, int w, int left, int top, int right, int bottom, byte[] color)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var i = (y * w + x) * 4;
                canvas[i] = color[0];
                canvas[i + 1] = color[1];
                canvas[i + 2] = color[2];
                canvas[i + 3] = 0xFF;
            }
        }
    }

    /// <summary>Box-filters the supersampled canvas down to the output size.</summary>
    private static byte[] Downsample(byte[] canvas, int canvasWidth, int outWidth, int outHeight)
    {
        var pixels = new byte[outWidth * outHeight * 4];
        for (var fy = 0; fy < outHeight; fy++)
        {
            for (var fx = 0; fx < outWidth; fx++)
            {
                int b = 0, g = 0, r = 0;
                for (var sy = 0; sy < SuperSample; sy++)
                {
                    for (var sx = 0; sx < SuperSample; sx++)
                    {
                        var i = ((fy * SuperSample + sy) * canvasWidth + fx * SuperSample + sx) * 4;
                        b += canvas[i];
                        g += canvas[i + 1];
                        r += canvas[i + 2];
                    }
                }

                const int Samples = SuperSample * SuperSample;
                var o = (fy * outWidth + fx) * 4;
                pixels[o] = (byte)(b / Samples);
                pixels[o + 1] = (byte)(g / Samples);
                pixels[o + 2] = (byte)(r / Samples);
                pixels[o + 3] = 0xFF;
            }
        }

        return pixels;
    }

    private static void DrawBorder(byte[] pixels, int width, int height, int thickness)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= thickness && x < width - thickness && y >= thickness && y < height - thickness)
                {
                    continue;
                }

                var i = (y * width + x) * 4;
                pixels[i] = Border[0];
                pixels[i + 1] = Border[1];
                pixels[i + 2] = Border[2];
                pixels[i + 3] = 0xFF;
            }
        }
    }

    private static byte[] EncodePng(byte[] bgraPixels, int width, int height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask().GetAwaiter().GetResult();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, bgraPixels);
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

        var bytes = new byte[(int)stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        reader.LoadAsync((uint)bytes.Length).AsTask().GetAwaiter().GetResult();
        reader.ReadBytes(bytes);
        return bytes;
    }
}
