using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClaudeUsageDock.Services;

/// <summary>
/// Rasterizes the monthly usage trend into a GitHub-style heatmap PNG laid out as a
/// calendar: five Monday-aligned week rows by weekday columns (headed M T W T F S S),
/// month labels at the left where a new month starts, and a separated WK column of
/// week totals — so the one graphic reads day, week, and month usage. Rounded cells
/// step through a teal→cyan intensity ramp on a dark navy panel. Pixel work happens
/// on a plain BGRA buffer and the OS PNG encoder does the rest, so no drawing
/// library is pulled in. The panel bakes in its own dark surface so it reads the
/// same on light and dark themes.
/// </summary>
internal static class TrendChartRenderer
{
    /// <summary>Cells holding this instead of a usage value are not drawn at all (future days).</summary>
    public const double NotApplicable = -1;

    public const int WeekRows = 5;   // current week + the four before it
    public const int DayColumns = 7; // Mon..Sun

    /// <summary>Drawn at 3x and box-filtered down, which anti-aliases the rounded corners.</summary>
    private const int SuperSample = 3;

    // Geometry in display units; the output bitmap is this times the pixel ratio.
    private const int CellSize = 18;
    private const int CellGap = 3;
    private const int CellCornerRadius = 4;
    private const int Padding = 8;
    private const int GlyphScale = 2;
    private const int GlyphSize = 5 * GlyphScale;              // the font is a 5x5 pixel grid
    private const int GlyphAdvance = GlyphSize + GlyphScale;   // glyph plus inter-glyph spacing
    private const int LabelGap = 4;
    private const int TotalsGap = 8; // extra space setting the WK column apart from the day grid
    private const int GridLeft = Padding + 3 * GlyphAdvance + LabelGap; // room for a 3-letter month label
    private const int GridTop = Padding + GlyphSize + LabelGap;
    private const int TotalsLeft = GridLeft + DayColumns * (CellSize + CellGap) - CellGap + TotalsGap;
    public const int DisplayWidth = TotalsLeft + CellSize + Padding;
    public const int DisplayHeight = GridTop + WeekRows * CellSize + (WeekRows - 1) * CellGap + Padding;

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

    // 5x5 pixel font: the uppercase letters that weekday headers, month
    // abbreviations (invariant culture), and the WK header can need.
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = [".***.", "*...*", "*****", "*...*", "*...*"],
        ['B'] = ["****.", "*...*", "****.", "*...*", "****."],
        ['C'] = [".****", "*....", "*....", "*....", ".****"],
        ['D'] = ["****.", "*...*", "*...*", "*...*", "****."],
        ['E'] = ["*****", "*....", "***..", "*....", "*****"],
        ['F'] = ["*****", "*....", "***..", "*....", "*...."],
        ['G'] = [".****", "*....", "*..**", "*...*", ".***."],
        ['J'] = ["..***", "...*.", "...*.", "*..*.", ".**.."],
        ['K'] = ["*..*.", "*.*..", "**...", "*.*..", "*..*."],
        ['L'] = ["*....", "*....", "*....", "*....", "*****"],
        ['M'] = ["*...*", "**.**", "*.*.*", "*...*", "*...*"],
        ['N'] = ["*...*", "**..*", "*.*.*", "*..**", "*...*"],
        ['O'] = [".***.", "*...*", "*...*", "*...*", ".***."],
        ['P'] = ["****.", "*...*", "****.", "*....", "*...."],
        ['R'] = ["****.", "*...*", "****.", "*.*..", "*..*."],
        ['S'] = [".****", "*....", ".***.", "....*", "****."],
        ['T'] = ["*****", "..*..", "..*..", "..*..", "..*.."],
        ['U'] = ["*...*", "*...*", "*...*", "*...*", ".***."],
        ['V'] = ["*...*", "*...*", "*...*", ".*.*.", "..*.."],
        ['W'] = ["*...*", "*...*", "*.*.*", "**.**", "*...*"],
        ['Y'] = ["*...*", ".*.*.", "..*..", "..*..", "..*.."],
    };

    private const string WeekdayHeader = "MTWTFSS";

    /// <summary>
    /// Renders the heatmap to PNG bytes. <paramref name="dayCells"/> is indexed
    /// [week row (0 = oldest), weekday column (0 = Monday)] and holds non-negative
    /// usage amounts in any unit, or <see cref="NotApplicable"/> for days that
    /// haven't happened yet. <paramref name="weekTotals"/> holds one total per row
    /// for the WK column; <paramref name="rowLabels"/> holds an up-to-3-letter
    /// label per row or null (month names, shown where a month begins). Day cells
    /// and week totals are each leveled against their own maximum, quartered into
    /// the four ramp steps like GitHub's contribution graph. The output bitmap is
    /// <see cref="DisplayWidth"/> × <see cref="DisplayHeight"/> times
    /// <paramref name="pixelRatio"/>, so a ratio of 2 stays crisp on high-DPI
    /// screens when shown at the display size.
    /// </summary>
    public static byte[] Render(double[,] dayCells, double[] weekTotals, string?[] rowLabels, int pixelRatio = 2)
    {
        var unit = pixelRatio * SuperSample;
        var w = DisplayWidth * unit;
        var h = DisplayHeight * unit;
        var canvas = new byte[w * h * 4];
        FillRect(canvas, w, 0, 0, w, h, Surface);

        var maxDay = 0.0;
        for (var row = 0; row < WeekRows; row++)
        {
            for (var col = 0; col < DayColumns; col++)
            {
                maxDay = Math.Max(maxDay, dayCells[row, col]);
            }
        }

        var maxWeek = weekTotals.Max();

        for (var row = 0; row < WeekRows; row++)
        {
            var top = (GridTop + row * (CellSize + CellGap)) * unit;
            for (var col = 0; col < DayColumns; col++)
            {
                if (dayCells[row, col] is NotApplicable)
                {
                    continue;
                }

                FillRoundedRect(
                    canvas, w,
                    (GridLeft + col * (CellSize + CellGap)) * unit, top,
                    CellSize * unit, CellSize * unit, CellCornerRadius * unit,
                    CellRamp[Level(dayCells[row, col], maxDay)]);
            }

            FillRoundedRect(
                canvas, w,
                TotalsLeft * unit, top,
                CellSize * unit, CellSize * unit, CellCornerRadius * unit,
                CellRamp[Level(weekTotals[row], maxWeek)]);

            if (rowLabels[row] is { Length: > 0 and <= 3 } label)
            {
                var y = top + (CellSize - GlyphSize) / 2 * unit;
                DrawText(canvas, w, label, Padding * unit, y, unit);
            }
        }

        // Header row: one weekday initial centered over each day column, WK over the totals.
        for (var col = 0; col < DayColumns; col++)
        {
            var x = (GridLeft + col * (CellSize + CellGap) + (CellSize - GlyphSize) / 2) * unit;
            DrawText(canvas, w, WeekdayHeader[col].ToString(), x, Padding * unit, unit);
        }

        DrawText(canvas, w, "WK", (TotalsLeft - (2 * GlyphAdvance - CellSize) / 2) * unit, Padding * unit, unit);

        var outWidth = DisplayWidth * pixelRatio;
        var outHeight = DisplayHeight * pixelRatio;
        var pixels = Downsample(canvas, w, outWidth, outHeight);
        DrawBorder(pixels, outWidth, outHeight, pixelRatio);
        return EncodePng(pixels, outWidth, outHeight);
    }

    /// <summary>0 for nothing, else the value's quarter of the maximum, mapped to ramp steps 1–4.</summary>
    private static int Level(double value, double max) =>
        value <= 0 || max <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(value / max * (CellRamp.Length - 1)), 1, CellRamp.Length - 1);

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
            if (Font.TryGetValue(ch, out var glyph))
            {
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
            }

            pen += GlyphAdvance * unit;
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
