using Windows.Graphics.Imaging;

namespace ClaudeUsageDock.Services;

/// <summary>
/// Rasterizes one thin rounded progress bar PNG for the Usage tab: a dark pill
/// track with a colored fill sized to the quota remaining, stepping blue → amber
/// → red as that remaining runs low. Corners outside the pill stay transparent so
/// the bar sits directly on the card background; the baked colors read the same on
/// light and dark themes, matching the heatmap panel's approach.
/// </summary>
internal static class BarRenderer
{
    public const int DisplayWidth = 380;
    public const int DisplayHeight = 8;
    private const int CornerRadius = DisplayHeight / 2; // full pill

    // Fill color bands, in percent remaining — the fill turns warning colors as
    // the remaining quota drops toward zero (the mirror of 75%/90% used).
    private const double AmberBelow = 25;
    private const double RedBelow = 10;

    // Colors stored B,G,R to match the buffer layout.
    private static readonly byte[] Track = [0x45, 0x3B, 0x33];  // #333B45
    private static readonly byte[] Blue = [0xD6, 0x7C, 0x2E];   // #2E7CD6
    private static readonly byte[] Amber = [0x3D, 0xA3, 0xE8];  // #E8A33D
    private static readonly byte[] Red = [0x56, 0x48, 0xE7];    // #E74856

    /// <summary>
    /// Renders the bar for a remaining percentage (0–100): the fill spans that
    /// fraction of the track and colors by how little is left. The output bitmap
    /// is <see cref="DisplayWidth"/> × <see cref="DisplayHeight"/> times
    /// <paramref name="pixelRatio"/>, so a ratio of 2 stays crisp on high-DPI
    /// screens when shown at the display size.
    /// </summary>
    public static byte[] Render(double percentRemaining, int pixelRatio = 2)
    {
        var remaining = Math.Clamp(percentRemaining, 0, 100);
        var unit = pixelRatio * Rasterizer.SuperSample;
        var w = DisplayWidth * unit;
        var h = DisplayHeight * unit;
        var canvas = new byte[w * h * 4]; // zero-initialized = transparent outside the pill

        Rasterizer.FillRoundedRect(canvas, w, 0, 0, w, h, CornerRadius * unit, Track);

        if (remaining > 0)
        {
            // Never narrower than the pill caps, so a small value still reads as a dot.
            var fillWidth = Math.Max((int)Math.Round(remaining / 100 * DisplayWidth), DisplayHeight);
            var fill = remaining <= RedBelow ? Red : remaining <= AmberBelow ? Amber : Blue;
            Rasterizer.FillRoundedRect(canvas, w, 0, 0, fillWidth * unit, h, CornerRadius * unit, fill);
        }

        var outWidth = DisplayWidth * pixelRatio;
        var outHeight = DisplayHeight * pixelRatio;
        var pixels = Rasterizer.Downsample(canvas, w, outWidth, outHeight);
        return Rasterizer.EncodePng(pixels, outWidth, outHeight, BitmapAlphaMode.Premultiplied);
    }
}
