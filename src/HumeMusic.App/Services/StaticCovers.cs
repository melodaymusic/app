using System.Security.Cryptography;
using System.Text;
using HumeMusic.Controls;
using HumeMusic.Core;
using HumeMusic.Core.Artwork;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace HumeMusic.Services;

/// <summary>
/// A still cover for the things Meloday Music makes itself (stations, mixes, tags, Meloday), whose
/// cards move: drawn as a picture in the same typography as Meloday's own cover (the name top right,
/// what it is bottom left), for places that show a still (the pins on Home and in the sidebar, and
/// "Jump back in"). Drawn once and kept on disk.
/// </summary>
public sealed class StaticCovers
{
    // Bumped when the drawing changes, so covers made the old way are drawn again.
    private const int Version = 11;

    private readonly ArtworkService _artwork;
    private readonly MelodayService _meloday;
    private readonly ILogger<StaticCovers> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<string?>> _loads = new();

    public StaticCovers(ArtworkService artwork, MelodayService meloday, ILogger<StaticCovers> log)
    {
        _artwork = artwork;
        _meloday = meloday;
        _log = log;
    }

    private static string Folder => Path.Combine(AppPaths.Root, "cache", "covers");

    /// <summary>Whether this kind of item has a still cover drawn for it (Plex's own items have their pictures).</summary>
    public static bool Applies(MediaItem item) => (item is PinnedCover c ? c.Target : item) is Station { IsBuilder: false } or ArtistMix or TagMix or BuiltMix or TagItem;

    /// <summary>The still cover's file for the item (drawn if it isn't yet), or null if there isn't one or it couldn't be drawn.</summary>
    public Task<string?> GetAsync(MediaItem item)
    {
        if (item is PinnedCover cover) item = cover.Target;
        if (!Applies(item)) return Task.FromResult<string?>(null);
        var (name, label) = Text(item);
        // Meloday's cover follows the mix (its sky is the part of the day's), so it is drawn again for each.
        var mix = (item as Station)?.StationKind == StationKind.Meloday ? _meloday.Current : null;
        var key = $"{item.RatingKey}|{name}|{label}|{mix?.Period.Name}|{mix?.PeriodStart:O}|v{Version}";
        var path = Path.Combine(Folder, $"cover-{Hash(key)}.png");
        if (File.Exists(path)) return Task.FromResult<string?>(path);
        // The tag's own colour is read here, on the caller's (interface) thread: brushes can't be read from another.
        var tint = TagTint(item);
        return _loads.GetOrAdd(path, _ => DrawAsync(item, name, label, path, mix, tint)).ContinueWith(t =>
        {
            _loads.TryRemove(path, out _);
            return t.IsCompletedSuccessfully ? t.Result : null;
        }, TaskScheduler.Default);
    }

    /// <summary>The name and label a cover sets: "Library" and "Radio", "Pop" and "Mix"...</summary>
    private static (string Name, string Label) Text(MediaItem item) => item switch
    {
        Station { StationKind: StationKind.Meloday } => ("Meloday", "Meloday"),
        Station s => (Helpers.Format.StationCoverName(s), Helpers.Format.StationCoverLabel(s)),
        BuiltMix { RatingKey: not "mix:discovery" } => (item.Title.EndsWith(" Radio", StringComparison.Ordinal) ? item.Title[..^6] : item.Title, "Radio"),
        ArtistMix or TagMix or BuiltMix => (item.Title.EndsWith(" Mix", StringComparison.Ordinal) ? item.Title[..^4] : item.Title, "Mix"),
        TagItem t => (t.Title, t.TagKind.ToString()),
        _ => (item.Title, string.Empty),
    };

    private static uint TagTint(MediaItem item)
    {
        var c = Helpers.TagColors.ColorFor(item is TagMix t ? t.Tag : item.Title);
        return (uint)(c.R << 16 | c.G << 8 | c.B);
    }

    private async Task<string?> DrawAsync(MediaItem item, string name, string label, string path, MelodayMix? mix, uint tint)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Folder);
            if (item is Station { StationKind: StationKind.Meloday })
            {
                var period = mix?.Period ?? MelodayPeriods.At(DateTime.Now);
                return await MelodayCover.RenderToAsync(period, mix?.Title ?? "Meloday", path, _log).ConfigureAwait(false);
            }

            var device = CanvasDevice.GetSharedDevice();
            const float size = 1000;
            using var target = new CanvasRenderTarget(device, size, size, 96);
            var named = true;
            using (var ds = target.CreateDrawingSession())
            {
                if (item is Station station)
                {
                    // A station's colour field, still, as its moving card begins.
                    ds.Clear(Rgb(StationColours.Family(station).Original));
                    var still = Path.Combine(AppContext.BaseDirectory, "Assets", "Radio", StationColours.Field(station) + "_still.png");
                    if (File.Exists(still))
                    {
                        using var field = await CanvasBitmap.LoadAsync(device, still);
                        var side = Math.Min(field.SizeInPixels.Width, field.SizeInPixels.Height);
                        var source = new Windows.Foundation.Rect((field.SizeInPixels.Width - side) / 2.0, (field.SizeInPixels.Height - side) / 2.0, side, side);
                        ds.DrawImage(field, new Windows.Foundation.Rect(0, 0, size, size), source);
                    }
                    // The neutral field takes the station's colour, laid over it as the moving card lays it.
                    if (StationColours.Tint(station) is { } wash)
                        ds.FillRectangle(0, 0, size, size, Color.FromArgb((byte)Math.Round(Core.Radio.StationBrand.TintOpacity * 255), (byte)(wash >> 16 & 0xFF), (byte)(wash >> 8 & 0xFF), (byte)(wash & 0xFF)));
                }
                else if (item is ArtistMix or TagMix or BuiltMix && await MixCoverData.LoadAsync(item, tint).ConfigureAwait(false) is { } mixCover)
                {
                    // A mix, as its card shows it: the main artist in the middle, two others around them.
                    ds.Clear(Rgb(mixCover.Colour));
                    var artists = new[] { mixCover.Main }.Concat(mixCover.Orbit).ToList();
                    for (var i = 0; i < artists.Count; i++)
                    {
                        var (x, y, d) = MixCircles.StillSlots[i];
                        // The small circles are ringed in the field's colour, where they overlap the main one.
                        if (i > 0) ds.FillCircle((float)(x * size), (float)(y * size), (float)((d / 2 + MixCircles.RingShare) * size), Rgb(mixCover.Colour));
                        await DrawCircleAsync(device, ds, artists[i].Thumb, (float)(x * size), (float)(y * size), (float)(d * size / 2)).ConfigureAwait(false);
                    }
                    // Named in the bottom left, as the card is.
                    MelodayCover.DrawTitleBottomLeft(ds, mixCover.Info.Title, size, _log);
                    named = false;
                }
                else
                {
                    ds.Clear(Rgb(await ColourAsync(item, tint).ConfigureAwait(false)));
                }
                if (named) MelodayCover.DrawLabels(ds, name, label, size, size, _log);
            }
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
            return path;
        }
        // The cover is decoration: whatever goes wrong drawing it, the card shows without it.
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A still cover for {Item} couldn't be drawn", item.Title);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>An artist's photo as a circle (a grey one when there's no photo).</summary>
    private async Task DrawCircleAsync(CanvasDevice device, CanvasDrawingSession ds, string? thumb, float cx, float cy, float radius)
    {
        var photo = await _artwork.GetLocalPathAsync(thumb, 400).ConfigureAwait(false);
        using var circle = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateCircle(device, cx, cy, radius);
        if (photo is null || !File.Exists(photo))
        {
            ds.FillGeometry(circle, Color.FromArgb(70, 255, 255, 255));
            return;
        }
        using var bitmap = await CanvasBitmap.LoadAsync(device, photo);
        var side = Math.Min(bitmap.SizeInPixels.Width, bitmap.SizeInPixels.Height);
        var source = new Windows.Foundation.Rect((bitmap.SizeInPixels.Width - side) / 2.0, (bitmap.SizeInPixels.Height - side) / 2.0, side, side);
        using (ds.CreateLayer(1f, circle))
            ds.DrawImage(bitmap, new Windows.Foundation.Rect(cx - radius, cy - radius, radius * 2, radius * 2), source);
    }

    /// <summary>A mix takes the colour of its artist's photo; a tag its own colour; both matched to the Meloday palette.</summary>
    private async Task<uint> ColourAsync(MediaItem item, uint tint)
    {
        uint? baseColour = null;
        if (item is ArtistMix mix) baseColour = await _artwork.GetDominantColorAsync(mix.Artist.Thumb).ConfigureAwait(false);
        return MelodayPalette.ForWhiteText(baseColour ?? tint);
    }

    private static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
}
