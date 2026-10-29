using MelodayMusic.Core;
using MelodayMusic.Core.Covers;
using MelodayMusic.Core.Playlists;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Windows.UI;

namespace MelodayMusic.Services;

/// <summary>The photo of the artist a Made by Meloday playlist starts with, kept as a local file.</summary>
public sealed record CoverPhoto(string Artist, string Path);

/// <summary>
/// A Made by Meloday playlist's cover: the photo of the artist it starts with, filling the square, a dark fade at the foot,
/// the name bottom left, and the edge bars in the colour that best stands for the photo (<see cref="PhotoColour"/>).
/// It changes when the playlist does and starts with someone else. With no photo to hand, the flat colour cover.
/// </summary>
public static class MadePlaylistCover
{
    private const int Version = 7;
    private static string Folder => Path.Combine(AppPaths.Root, "made");

    public static async Task<string?> RenderAsync(PlaylistRecipe recipe, CoverPhoto? photo, ILogger log)
    {
        var ground = MadePlaylists.CoverColour(recipe);
        var which = photo is null ? $"{ground:x6}" : "p" + MadePlaylists.Stable(photo.Artist + "|" + photo.Path).ToString("x8");
        var path = Path.Combine(Folder, $"cover-{recipe.Id}-{which}-v{Version}.png");
        if (File.Exists(path)) return path;
        try
        {
            Directory.CreateDirectory(Folder);
            const float size = 1000;
            var device = CanvasDevice.GetSharedDevice();
            using var picture = photo is null ? null : await LoadAsync(device, photo.Path, log);
            using var target = new CanvasRenderTarget(device, size, size, 96);
            using (var ds = target.CreateDrawingSession())
            {
                if (picture is null)
                {
                    ds.Clear(Color.FromArgb(255, (byte)(ground >> 16), (byte)(ground >> 8), (byte)ground));
                    var baseline = MelodayCover.DrawTitleBottomLeft(ds, recipe.Title, size, log);
                    CoverEdges.Draw(ds, ground, size, baseline);
                }
                else
                {
                    var edge = EdgeColour(device, picture);
                    DrawPhoto(ds, picture, size);
                    var baseline = MelodayCover.DrawTitleBottomLeft(ds, recipe.Title, size, log);
                    CoverEdges.DrawIn(ds, edge, size, baseline);
                }
            }
            await CoverSymbol.DrawAsync(target);
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
            foreach (var old in Directory.EnumerateFiles(Folder, $"cover-{recipe.Id}-*.png"))
            {
                if (string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(old); } catch (IOException) { }
            }
            return path;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "The cover for {Playlist} couldn't be drawn", recipe.Title);
            return null;
        }
    }

    private static async Task<CanvasBitmap?> LoadAsync(CanvasDevice device, string path, ILogger log)
    {
        try
        {
            return await CanvasBitmap.LoadAsync(device, path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            log.LogInformation("The artist photo for a cover couldn't be read ({Message}); using the colour cover", ex.Message);
            return null;
        }
    }

    /// <summary>The photo shrunk to 120 pixels square, as the colour picker wants it.</summary>
    internal static uint EdgeColour(CanvasDevice device, CanvasBitmap picture)
    {
        const int side = 120;
        using var small = new CanvasRenderTarget(device, side, side, 96);
        using (var ds = small.CreateDrawingSession()) DrawPhoto(ds, picture, side, fade: false);
        return PhotoColour.Pick(small.GetPixelBytes(), side, side);
    }

    /// <summary>The photo filling the square, centred, with a dark fade at the foot so the white name reads on any photo.</summary>
    internal static void DrawPhoto(CanvasDrawingSession ds, CanvasBitmap picture, float size, bool fade = true)
    {
        var bounds = picture.Size;
        var side = Math.Min(bounds.Width, bounds.Height);
        var from = new Windows.Foundation.Rect((bounds.Width - side) / 2, (bounds.Height - side) / 2, side, side);
        ds.DrawImage(picture, new Windows.Foundation.Rect(0, 0, size, size), from);
        if (!fade) return;
        var stops = new[]
        {
            new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0, 0, 0, 0) },
            new CanvasGradientStop { Position = 0.55f, Color = Color.FromArgb(40, 0, 0, 0) },
            new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(175, 0, 0, 0) },
        };
        using var brush = new CanvasLinearGradientBrush(ds, stops)
        {
            StartPoint = new System.Numerics.Vector2(0, size * 0.42f),
            EndPoint = new System.Numerics.Vector2(0, size),
        };
        ds.FillRectangle(0, 0, size, size, brush);
    }
}
