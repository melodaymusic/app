using System.Numerics;
using MelodayMusic.Controls;
using MelodayMusic.Core;
using MelodayMusic.Core.Covers;
using MelodayMusic.Core.Meloday;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Text;

namespace MelodayMusic.Services;

public static class MelodayCover
{
    private static Windows.UI.Color Rgb(uint rgb, double opacity) =>
        Windows.UI.Color.FromArgb((byte)Math.Round(opacity * 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static readonly string[] Fonts =
    [
        new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "DMSans.ttf")).AbsoluteUri + "#DM Sans",
        "ms-appx:///Assets/Fonts/DMSans.ttf#DM Sans",
        "Segoe UI Variable Display",
    ];
    private static string? _font;
    internal static string Font => Fonts[0];

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

    private static CanvasTextLayout Fit(CanvasDrawingSession ds, string text, double usual, double lineRatio, double baselineRatio, Windows.UI.Text.FontWeight weight,
        CanvasHorizontalAlignment align, float box, float boxHeight, int maxLines, Microsoft.Extensions.Logging.ILogger log)
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
                        if (whole.LineMetrics.Length > maxLines || whole.LayoutBounds.Height > boxHeight) return false;
                    foreach (var word in words)
                    {
                        using var one = new CanvasTextLayout(ds, word, format, 100000f, 1000f);
                        if (one.LayoutBounds.Width > box + 0.5) return false;
                    }
                    return true;
                }
                var size = CoverFit.Size(usual, Fits);
                using var chosen = Format(font, size, lineRatio, baselineRatio, weight, align);
                chosen.VerticalAlignment = align == CanvasHorizontalAlignment.Left ? CanvasVerticalAlignment.Bottom : CanvasVerticalAlignment.Top;
                var layout = new CanvasTextLayout(ds, text, chosen, box, boxHeight);
                if (_font is null) Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(log, "Meloday: covers are set in {Font}", font);
                _font = font;
                return layout;
            }
            catch (ArgumentException) when (font != Fonts[^1])
            {
            }
        }
        throw new InvalidOperationException("No typeface for the Meloday cover.");
    }

    /// <summary>Draws the playlist label top right and the name bottom left; returns the name's baseline, if there is a name.</summary>
    internal static float? DrawLabels(CanvasDrawingSession ds, string label, string name, float width, float height, Microsoft.Extensions.Logging.ILogger log,
        Windows.UI.Color? ink = null, int nameLines = CoverType.NameLines)
    {
        var colour = ink ?? Colors.White;
        var k = width / (float)CoverType.Unit;
        var box = (float)CoverType.LabelWidth * k;
        if (!string.IsNullOrWhiteSpace(label))
        {
            using var layout = Fit(ds, label, CoverType.LabelSize * k, CoverType.LabelLine / CoverType.LabelSize, CoverType.LabelBaseline / CoverType.LabelSize,
                CoverType.LabelWeight, CanvasHorizontalAlignment.Right, box, (float)CoverType.BoxHeight * k, CoverType.LabelLines, log);
            ds.DrawTextLayout(layout, new Vector2(width - (float)CoverType.LabelRight * k - box, (float)CoverType.LabelTop * k), colour);
        }
        return DrawName(ds, name, width, height, log, colour, nameLines);
    }

    private static float? DrawName(CanvasDrawingSession ds, string name, float width, float height, Microsoft.Extensions.Logging.ILogger log,
        Windows.UI.Color? ink = null, int lines = CoverType.NameLines)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var k = width / (float)CoverType.Unit;
        using var layout = Fit(ds, name, CoverType.NameSize * k, CoverType.NameLine / CoverType.NameSize, CoverType.NameBaseline / CoverType.NameSize,
            CoverType.NameWeight, CanvasHorizontalAlignment.Left, (float)CoverType.NameWidth * k, (float)CoverType.BoxHeight * k, lines, log);
        ds.DrawTextLayout(layout, new Vector2((float)CoverType.NameLeft * k, height - (float)(CoverType.NameBottom + CoverType.BoxHeight) * k), ink ?? Colors.White);
        return height - (float)CoverType.NameBottom * k
            - (float)((CoverType.NameLine - CoverType.NameBaseline) / CoverType.NameSize) * layout.DefaultFontSize;
    }

    internal static float? DrawTitleBottomLeft(CanvasDrawingSession ds, string title, float size, Microsoft.Extensions.Logging.ILogger log) =>
        string.IsNullOrWhiteSpace(title) ? null : DrawName(ds, title, size, size, log);

    internal const string FilePrefix = "cover-v18-";

    public static async Task<string?> RenderAsync(MelodayPeriod period, string title, DateTime now, Microsoft.Extensions.Logging.ILogger log)
    {
        Directory.CreateDirectory(AppPaths.Meloday);
        var path = Path.Combine(AppPaths.Meloday, $"{FilePrefix}{now:yyyyMMdd-HHmmss}.png");
        var drawn = await RenderToAsync(period, title, path, log).ConfigureAwait(false);
        if (drawn is null) return null;
        foreach (var old in Directory.EnumerateFiles(AppPaths.Meloday, "cover-*.png"))
        {
            if (string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); } catch (IOException) { }
        }
        return drawn;
    }

    public static async Task<string?> RenderToAsync(MelodayPeriod period, string title, string path, Microsoft.Extensions.Logging.ILogger log)
    {
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            const float width = 1000, height = 1000;
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
                DrawLabels(ds, "Meloday", name, width, height, log);
            }

            await CoverSymbol.DrawAsync(target, CoverEdge.Colour(style.Background));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
            return path;
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(log, ex, "Meloday: the cover couldn't be drawn");
            return null;
        }
    }
}
