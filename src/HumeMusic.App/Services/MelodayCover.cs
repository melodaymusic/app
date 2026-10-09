using System.Numerics;
using HumeMusic.Controls;
using HumeMusic.Core;
using HumeMusic.Core.Covers;
using HumeMusic.Core.Meloday;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Text;

namespace HumeMusic.Services;

/// <summary>
/// A Meloday's cover, laid out as the script lays it: the part of the day's sun (MelodaySun), the mix's name (without
/// "Meloday for") set right-aligned in the top right, and "Meloday" in the bottom left. Set in the
/// app's own typeface (Manrope), and saved as a PNG for the page and, when kept on Plex, the playlist.
/// </summary>
public static class MelodayCover
{
    // Win2D reads a font file by address; an unpackaged app can't always resolve ms-appx, so the file's
    // own path comes first, then the package address, then Windows' own display face.
    private static Windows.UI.Color Rgb(uint rgb, double opacity) =>
        Windows.UI.Color.FromArgb((byte)Math.Round(opacity * 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static readonly string[] Fonts =
    [
        new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Manrope-Variable.ttf")).AbsoluteUri + "#Manrope",
        "ms-appx:///Assets/Fonts/Manrope-Variable.ttf#Manrope",
        "Segoe UI Variable Display",
    ];
    private static string? _font;

    private static CanvasTextFormat Format(string font, double size, double lineRatio, double baselineRatio, Windows.UI.Text.FontWeight weight, CanvasHorizontalAlignment align) => new()
    {
        FontFamily = font,
        FontSize = (float)size,
        FontWeight = weight,
        HorizontalAlignment = align,
        WordWrapping = CanvasWordWrapping.Wrap,
        LineSpacing = (float)(size * lineRatio),
        LineSpacingBaseline = (float)(size * baselineRatio),
    };

    /// <summary>
    /// A text set in its box, in the first typeface Win2D can load: at its usual size, or just small enough to fit the box when it
    /// wouldn't (no more than <paramref name="maxLines"/> lines, and no word wider than the box, which would be broken across lines).
    /// </summary>
    private static CanvasTextLayout Fit(CanvasDrawingSession ds, string text, double usual, double lineRatio, double baselineRatio, Windows.UI.Text.FontWeight weight,
        CanvasHorizontalAlignment align, float box, int maxLines, Microsoft.Extensions.Logging.ILogger log)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var font in _font is null ? Fonts : [_font])
        {
            try
            {
                bool Fits(double size)
                {
                    using var format = Format(font, size, lineRatio, baselineRatio, weight, align);
                    using (var whole = new CanvasTextLayout(ds, text, format, box, 100000f))
                        if (whole.LineMetrics.Length > maxLines) return false;
                    foreach (var word in words)
                    {
                        using var one = new CanvasTextLayout(ds, word, format, 100000f, 1000f);
                        if (one.LayoutBounds.Width > box + 0.5) return false;
                    }
                    return true;
                }
                var size = CoverFit.Size(usual, Fits);
                using var chosen = Format(font, size, lineRatio, baselineRatio, weight, align);
                var layout = new CanvasTextLayout(ds, text, chosen, box, 100000f);
                if (_font is null) Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(log, "Meloday: covers are set in {Font}", font);
                _font = font;
                return layout;
            }
            catch (ArgumentException) when (font != Fonts[^1])
            {
                // Not loadable here: try the next.
            }
        }
        throw new InvalidOperationException("No typeface for the Meloday cover.");
    }

    /// <summary>
    /// The typography of every cover Meloday Music draws (see CoverType): the name right-aligned in the top right and the
    /// label in the bottom left, each in a box 760 wide at a fixed place and size (smaller only to fit), in a 1000-unit square
    /// scaled to <paramref name="width"/>. Either may be empty.
    /// </summary>
    internal static void DrawLabels(CanvasDrawingSession ds, string name, string label, float width, float height, Microsoft.Extensions.Logging.ILogger log)
    {
        var k = width / (float)CoverType.Unit;
        var box = (float)CoverType.BoxWidth * k;
        if (!string.IsNullOrWhiteSpace(name))
        {
            using var layout = Fit(ds, name, CoverType.NameSize * k, CoverType.NameLine / CoverType.NameSize, CoverType.NameBaseline / CoverType.NameSize,
                FontWeights.ExtraBold, CanvasHorizontalAlignment.Right, box, CoverType.NameLines, log);
            ds.DrawTextLayout(layout, new Vector2(width - (float)CoverType.NameRight * k - box, (float)CoverType.NameTop * k), Colors.White);
        }
        DrawLabel(ds, label, width, height, log);
    }

    /// <summary>The bottom-left text of a cover: its box's bottom edge is 100 up from the bottom, so a longer text grows upward.</summary>
    private static void DrawLabel(CanvasDrawingSession ds, string label, float width, float height, Microsoft.Extensions.Logging.ILogger log)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        var k = width / (float)CoverType.Unit;
        using var layout = Fit(ds, label, CoverType.LabelSize * k, CoverType.LabelLine / CoverType.LabelSize, CoverType.LabelBaseline / CoverType.LabelSize,
            FontWeights.Bold, CanvasHorizontalAlignment.Left, (float)CoverType.BoxWidth * k, CoverType.LabelLines, log);
        var lines = layout.LineMetrics.Length;
        ds.DrawTextLayout(layout, new Vector2((float)CoverType.LabelLeft * k, height - (float)CoverType.LabelBottom * k - lines * layout.LineSpacing), Colors.White);
    }

    /// <summary>
    /// A mix's name set on its still cover in the bottom left (over a gentle shade rising from that corner), where every cover
    /// has its label: same box, same size, smaller only when it wouldn't fit. <paramref name="size"/> is the side of the square cover.
    /// </summary>
    internal static void DrawTitleBottomLeft(CanvasDrawingSession ds, string title, float size, Microsoft.Extensions.Logging.ILogger log)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        using (var shade = new CanvasRadialGradientBrush(ds, Windows.UI.Color.FromArgb(0x99, 0, 0, 0), Windows.UI.Color.FromArgb(0, 0, 0, 0))
        {
            Center = new Vector2(0, size),
            RadiusX = size * 0.95f,
            RadiusY = size * 0.95f,
        })
            ds.FillRectangle(0, 0, size, size, shade);
        DrawLabel(ds, title, size, size, log);
    }

    /// <summary>The cover for a mix, saved as a new file in Meloday's folder (the old ones cleared away).</summary>
    public static async Task<string?> RenderAsync(MelodayPeriod period, string title, DateTime now, Microsoft.Extensions.Logging.ILogger log)
    {
        Directory.CreateDirectory(AppPaths.Meloday);
        // A new file for each mix (so no view keeps showing an old one), and the old ones cleared away.
        var path = Path.Combine(AppPaths.Meloday, $"cover-{now:yyyyMMdd-HHmmss}.png");
        var drawn = await RenderToAsync(period, title, path, log).ConfigureAwait(false);
        if (drawn is null) return null;
        foreach (var old in Directory.EnumerateFiles(AppPaths.Meloday, "cover-*.png"))
        {
            if (string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); } catch (IOException) { }
        }
        return drawn;
    }

    /// <summary>The cover drawn to <paramref name="path"/>: the part of the day's sky and sun, the mix's name, "Meloday".</summary>
    public static async Task<string?> RenderToAsync(MelodayPeriod period, string title, string path, Microsoft.Extensions.Logging.ILogger log)
    {
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            // The part of the day's sky and sun (MelodaySun), still: the app animates it, the picture holds it.
            const float width = 1000, height = 1000;
            // The script's measures are for a 1000px cover.
            const float k = 1;
            var style = MelodaySun.For(period.Name);

            using var target = new CanvasRenderTarget(device, width, height, 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Rgb(style.Background, 1));
                var centre = new Vector2((float)(style.X * width), (float)(style.Y * height));
                var sun = (float)(MelodaySunStyle.SunSize * width);
                foreach (var ring in style.Rings)
                    ds.FillCircle(centre, (float)(sun * ring.Size / 2), Rgb(ring.Colour, ring.Opacity));
                var name = title.StartsWith(MelodayNaming.Prefix, StringComparison.OrdinalIgnoreCase) ? title[MelodayNaming.Prefix.Length..] : title;
                DrawLabels(ds, name, "Meloday", width, height, log);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
            return path;
        }
        // The cover is decoration: whatever goes wrong drawing it, the mix goes on without it.
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(log, ex, "Meloday: the cover couldn't be drawn");
            return null;
        }
    }
}
