using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using MelodayMusic.Core;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Covers;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;

namespace MelodayMusic.Services;

public static class PlaylistCover
{
    public static async Task<string?> RenderAsync(string identity, string title, ILogger log)
    {
        var folder = Path.Combine(AppPaths.ArtworkCache, "playlists");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + title)));
        var path = Path.Combine(folder, "v17-" + hash + ".png");
        if (File.Exists(path)) return path;
        try
        {
            var layout = PlaylistCoverLayout.For(identity);
            var palette = MelodayPalette.Families;
            using var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), 1000, 1000, 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Colour(palette[layout.Palette].Darker, 255));
                void Radial(CoverRadial radial, uint colour)
                {
                    using var brush = new CanvasRadialGradientBrush(ds, Colour(colour, 255), Colour(colour, 0))
                    {
                        Center = new Vector2((float)radial.X * 1000, (float)radial.Y * 1000),
                        RadiusX = (float)radial.Radius * 1000, RadiusY = (float)radial.Radius * 1000,
                    };
                    ds.FillRectangle(0, 0, 1000, 1000, brush);
                }
                var colours = layout.Colours.Where(index => index != layout.Palette).ToList();
                Radial(layout.First, palette[colours[0]].Original);
                Radial(layout.Second, palette[colours[1]].Original);
                Radial(layout.Third, palette[colours[colours.Count > 2 ? 2 : 0]].Lighter);
                var baseline = MelodayCover.DrawLabels(ds, "", title, 1000, 1000, log);
                CoverEdges.Draw(ds, palette[layout.Palette].Darker, 1000, baseline);
            }
            await CoverSymbol.DrawAsync(target);
            Directory.CreateDirectory(folder);
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
            return path;
        }
        catch (Exception ex) { log.LogWarning(ex, "Playlist cover could not be drawn"); return null; }
    }
    private static Windows.UI.Color Colour(uint rgb, byte alpha) => Windows.UI.Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
