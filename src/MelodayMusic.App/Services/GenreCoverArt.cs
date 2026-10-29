using MelodayMusic.Core;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Covers;
using MelodayMusic.Core.Playlists;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.UI;

namespace MelodayMusic.Services;

public static class GenreCoverArt
{
    private const int Version = 5;
    private const float Size = 1000;

    public static async Task<string?> RenderAsync(GenreCover genre, ILogger log)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "Genres", genre.File);
        var folder = Path.Combine(AppPaths.Shared, "made", "genres");
        var path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(genre.File)}-v{Version}.png");
        if (File.Exists(path)) return path;
        if (!File.Exists(source)) return null;
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            using var art = await CanvasBitmap.LoadAsync(device, source);
            using var target = new CanvasRenderTarget(device, Size, Size, 96);
            using (var ds = target.CreateDrawingSession())
                ds.DrawImage(art, new Rect(0, 0, Size, Size), new Rect(0, 0, art.SizeInPixels.Width, art.SizeInPixels.Height), 1f,
                    CanvasImageInterpolation.HighQualityCubic);
            var topRight = Average(target, new Rect(Size * 0.55, Size * 0.04, Size * 0.41, Size * 0.12));
            var bottomLeft = Average(target, new Rect(Size * 0.04, Size * 0.72, Size * 0.80, Size * 0.24));
            var bottom = Average(target, new Rect(0, Size * 0.94, Size, Size * 0.06));
            using (var ds = target.CreateDrawingSession())
            {
                MelodayCover.DrawLabels(ds, "Essentials", string.Empty, Size, Size, log, Ink(topRight));
                var baseline = MelodayCover.DrawLabels(ds, string.Empty, genre.Genre, Size, Size, log, Ink(bottomLeft), labelLines: 2);
                CoverEdges.Draw(ds, MelodayPalette.Nearest(bottom), Size, baseline);
            }
            await CoverSymbol.DrawAsync(target);
            Directory.CreateDirectory(folder);
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "The cover for {Genre} Essentials couldn't be drawn", genre.Genre);
            return null;
        }
    }

    private static Color Ink(uint ground) => DominantColor.Luminance(ground) >= 0.6 ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255);

    private static uint Average(CanvasRenderTarget target, Rect area)
    {
        var bytes = target.GetPixelBytes((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
        long b = 0, g = 0, r = 0;
        var count = Math.Max(1, bytes.Length / 4);
        for (var i = 0; i < bytes.Length; i += 4) { b += bytes[i]; g += bytes[i + 1]; r += bytes[i + 2]; }
        return (uint)((r / count) << 16 | (g / count) << 8 | (b / count));
    }
}
