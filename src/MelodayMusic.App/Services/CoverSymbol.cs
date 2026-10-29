using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Covers;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Svg;
using Windows.Foundation;
using Windows.UI;

namespace MelodayMusic.Services;

internal static class CoverSymbol
{
    internal const float Width = 120, Left = 70, Top = 70, BadgeSize = 76, BadgeGap = 18;
    internal const float Height = Width * 226f / 512;

    internal static uint Dominant(CanvasBitmap canvas)
    {
        using var sample = new CanvasRenderTarget(canvas.Device, 96, 96, 96);
        using (var ds = sample.CreateDrawingSession())
            ds.DrawImage(canvas, new Rect(0, 0, 96, 96));
        return DominantColor.Find(sample.GetPixelBytes()) ?? 0x2A2930;
    }

    private static uint Under(CanvasRenderTarget target, float height)
    {
        var scale = target.SizeInPixels.Width / (float)target.Size.Width;
        int x = (int)(Left * scale), y = (int)(Top * scale), w = Math.Max(1, (int)(Width * scale)), h = Math.Max(1, (int)(height * scale));
        var bytes = target.GetPixelBytes(x, y, w, h);
        long b = 0, g = 0, r = 0;
        var count = bytes.Length / 4;
        for (var i = 0; i < bytes.Length; i += 4) { b += bytes[i]; g += bytes[i + 1]; r += bytes[i + 2]; }
        return count == 0 ? 0 : (uint)((r / count) << 16 | (g / count) << 8 | (b / count));
    }

    internal static uint InkFor(CanvasRenderTarget target) => CoverInk.Symbol(Under(target, Height));

    internal static Task DrawAsync(CanvasRenderTarget target, uint? ink = null, string? badge = null)
    {
        var colour = ink ?? CoverInk.Symbol(Under(target, Height));
        using var symbol = CanvasSvgDocument.LoadFromXml(target.Device, MelodaySymbol.Xml(colour));
        using var ds = target.CreateDrawingSession();
        ds.DrawSvg(symbol, new Size(Width, Height), Left, Top);
        if (string.IsNullOrEmpty(badge)) return Task.CompletedTask;
        using var format = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat
        {
            FontFamily = MelodayCover.Font, FontSize = BadgeSize, FontWeight = Microsoft.UI.Text.FontWeights.Black,
            WordWrapping = Microsoft.Graphics.Canvas.Text.CanvasWordWrapping.NoWrap,
        };
        using var layout = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(ds, badge, format, 1000, 200);
        var bounds = layout.DrawBounds;
        var x = Left + Width + BadgeGap - (float)bounds.X;
        var y = Top + Height / 2 -(float)(bounds.Y + bounds.Height / 2);
        ds.DrawTextLayout(layout, x, y, Color.FromArgb(255, (byte)(colour >> 16), (byte)(colour >> 8), (byte)colour));
        return Task.CompletedTask;
    }
}
