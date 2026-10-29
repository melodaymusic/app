using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Covers;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Graphics.DirectX;
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

    internal static uint InkFor(CanvasRenderTarget target) => CoverInk.Symbol(Under(target, Width * 226f / 512));

    internal static async Task DrawAsync(CanvasRenderTarget target, uint? ink = null, string? badge = null)
    {
        using var source = await CanvasBitmap.LoadAsync(target.Device,
            Path.Combine(AppContext.BaseDirectory, "Assets", "MelodayMusic-symbol.png"));
        var sourceWidth = (int)source.SizeInPixels.Width;
        var sourceHeight = (int)source.SizeInPixels.Height;
        var height = Width * sourceHeight / sourceWidth;
        var pixels = source.GetPixelBytes();
        var colour = ink ?? CoverInk.Symbol(Under(target, height));
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            pixels[i] = (byte)((colour & 255) * alpha / 255);
            pixels[i + 1] = (byte)(((colour >> 8) & 255) * alpha / 255);
            pixels[i + 2] = (byte)(((colour >> 16) & 255) * alpha / 255);
        }
        using var symbol = CanvasBitmap.CreateFromBytes(target.Device, pixels, sourceWidth, sourceHeight,
            DirectXPixelFormat.B8G8R8A8UIntNormalized);
        using var ds = target.CreateDrawingSession();
        ds.DrawImage(symbol, new Rect(Left, Top, Width, height),
            new Rect(0, 0, sourceWidth, sourceHeight), 1, CanvasImageInterpolation.HighQualityCubic);
        if (string.IsNullOrEmpty(badge)) return;
        using var format = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat
        {
            FontFamily = MelodayCover.Font, FontSize = BadgeSize, FontWeight = Microsoft.UI.Text.FontWeights.Black,
            WordWrapping = Microsoft.Graphics.Canvas.Text.CanvasWordWrapping.NoWrap,
        };
        using var layout = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(ds, badge, format, 1000, 200);
        var bounds = layout.DrawBounds;
        var x = Left + Width + BadgeGap - (float)bounds.X;
        var y = Top + height / 2 - (float)(bounds.Y + bounds.Height / 2);
        ds.DrawTextLayout(layout, x, y, Color.FromArgb(255, (byte)(colour >> 16), (byte)(colour >> 8), (byte)colour));
    }
}
