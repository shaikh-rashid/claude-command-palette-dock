using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClaudeUsageDock.Services;

/// <summary>
/// Rasterizes the weekly usage trend into a GitHub-style contribution heatmap PNG:
/// weekday rows (labeled M/W/F) by 3-hour-slot columns (labeled 06/12/18), rounded
/// cells stepping through a teal→cyan intensity ramp on a dark navy panel. Pixel
/// work happens on a plain BGRA buffer and the OS PNG encoder does the rest, so no
/// drawing library is pulled in. The panel bakes in its own dark surface so it
/// reads the same on light and dark themes.
/// </summary>
internal static class TrendChartRenderer
{
    /// <summary>Drawn at 3x and box-filtered down, which anti-aliases the rounded corners.</summary>
    private const int SuperSample = 3;

    public const int Rows = 7;    // Mon..Sun
    public const int Columns = 8; // 3-hour slots, 00:00–24:00

    // Geometry in display units; the output bitmap is this times the pixel ratio.
    private const int CellSize = 13;
    private const int CellGap = 3;
    private const int CellCornerRadius = 3;
    private const int Padding = 8;
    private const int GlyphScale = 2;
    private const int GlyphSize = 5 * GlyphScale; // the font is a 5x5 pixel grid
    private const int LabelGap = 4;
    private const int GridLeft = Padding + GlyphSize + LabelGap;
    private const int GridTop = Padding + GlyphSize + LabelGap;
    public const int DisplayWidth = GridLeft + Columns * CellSize + (Columns - 1) * CellGap + Padding;
    public const int DisplayHeight = GridTop + Rows * CellSize + (Rows - 1) * CellGap + Padding;

    // Panel palette, stored B,G,R to match the buffer layout.
    private static readonly byte[] Surface = [0x2D, 0x22, 0x17]; // #17222D
    private static readonly byte[] Border = [0x7E, 0x77, 0x71];  // #71777E
    private static readonly byte[] Label = [0xA3, 0x96, 0x8A];   // #8A96A3
    // Empty cell plus the four intensity steps: one teal→cyan hue, dark to bright,
    // like GitHub's Less→More ramp but in this panel's color scheme.
    private static readonly byte[][] CellRamp =
    [
        [0x3C, 0x2F, 0x21], // #212F3C no recorded usage
        [0x55, 0x47, 0x1E], // #1E4755
        [0x82, 0x6B, 0x2A], // #2A6B82
        [0xBC, 0xA0, 0x43], // #43A0BC
        [0xE8, 0xD0, 0x62], // #62D0E8
    ];

    // 5x5 pixel font, just the glyphs the axis labels need.
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['M'] = ["*...*", "**.**", "*.*.*", "*...*", "*...*"],
        ['W'] = ["*...*", "*...*", "*.*.*", "**.**", "*...*"],
        ['F'] = ["*****", "*....", "****.", "*....", "*...."],
        ['0'] = [".***.", "*...*", "*...*", "*...*", ".***."],
        ['1'] = ["..*..", ".**..", "..*..", "..*..", ".***."],
        ['2'] = [".***.", "*...*", "...*.", "..*..", "*****"],
        ['6'] = [".***.", "*....", "****.", "*...*", ".***."],
        ['8'] = [".***.", "*...*", ".***.", "*...*", ".***."],
    };

    /// <summary>
    /// Renders the heatmap to PNG bytes. <paramref name="cells"/> is indexed
    /// [weekday row (0 = Monday), 3-hour slot column] and holds non-negative usage
    /// amounts in any unit; intensity is relative to the busiest cell, quartered
    /// into the four ramp steps like GitHub's contribution graph. The output
    /// bitmap is <see cref="DisplayWidth"/> × <see cref="DisplayHeight"/> times
    /// <paramref name="pixelRatio"/>, so a ratio of 2 stays crisp on high-DPI
    /// screens when shown at the display size.
    /// </summary>
    public static byte[] Render(double[,] cells, int pixelRatio = 2)
    {
        var unit = pixelRatio * SuperSample;
        var w = DisplayWidth * unit;
        var h = DisplayHeight * unit;
        var canvas = new byte[w * h * 4];
        FillRect(canvas, w, 0, 0, w, h, Surface);

        var max = 0.0;
        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns; col++)
            {
                max = Math.Max(max, cells[row, col]);
            }
        }

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns; col++)
            {
                var value = cells[row, col];
                var level = value <= 0 || max <= 0
                    ? 0
                    : Math.Clamp((int)Math.Ceiling(value / max * (CellRamp.Length - 1)), 1, CellRamp.Length - 1);
                FillRoundedRect(
                    canvas, w,
                    (GridLeft + col * (CellSize + CellGap)) * unit,
                    (GridTop + row * (CellSize + CellGap)) * unit,
                    CellSize * unit,
                    CellSize * unit,
                    CellCornerRadius * unit,
                    CellRamp[level]);
            }
        }

        // Column labels: slot start hours, sparse like GitHub's month row.
        foreach (var (text, col) in new[] { ("06", 2), ("12", 4), ("18", 6) })
        {
            DrawText(canvas, w, text, (GridLeft + col * (CellSize + CellGap)) * unit, Padding * unit, unit);
        }

        // Row labels: every other weekday, GitHub's Mon/Wed/Fri convention.
        foreach (var (glyph, row) in new[] { ('M', 0), ('W', 2), ('F', 4) })
        {
            var y = (GridTop + row * (CellSize + CellGap) + (CellSize - GlyphSize) / 2) * unit;
            DrawText(canvas, w, glyph.ToString(), Padding * unit, y, unit);
        }

        var outWidth = DisplayWidth * pixelRatio;
        var outHeight = DisplayHeight * pixelRatio;
        var pixels = Downsample(canvas, w, outWidth, outHeight);
        DrawBorder(pixels, outWidth, outHeight, pixelRatio);
        return EncodePng(pixels, outWidth, outHeight);
    }

    private static void FillRect(byte[] canvas, int w, int left, int top, int width, int height, byte[] color)
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

    private static void FillRoundedRect(byte[] canvas, int w, int left, int top, int width, int height, int radius, byte[] color)
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

    private static void DrawText(byte[] canvas, int w, string text, int left, int top, int unit)
    {
        var pixel = GlyphScale * unit; // one font pixel, in canvas pixels
        var pen = left;
        foreach (var ch in text)
        {
            var glyph = Font[ch];
            for (var gy = 0; gy < 5; gy++)
            {
                for (var gx = 0; gx < 5; gx++)
                {
                    if (glyph[gy][gx] == '*')
                    {
                        FillRect(canvas, w, pen + gx * pixel, top + gy * pixel, pixel, pixel, Label);
                    }
                }
            }

            pen += (GlyphSize + GlyphScale) * unit;
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
