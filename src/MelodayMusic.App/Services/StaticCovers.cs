using System.Security.Cryptography;
using System.Text;
using MelodayMusic.Controls;
using MelodayMusic.Core;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace MelodayMusic.Services;

public sealed class StaticCovers
{
    private const int Version = 30;

    private readonly ArtworkService _artwork;
    private readonly MelodayService _meloday;
    private readonly ShapeCovers _shapes;
    private readonly ILogger<StaticCovers> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<string?>> _loads = new();

    public StaticCovers(ArtworkService artwork, MelodayService meloday, ShapeCovers shapes, ILogger<StaticCovers> log)
    {
        _artwork = artwork;
        _meloday = meloday;
        _shapes = shapes;
        _log = log;
    }

    private static string Folder => Path.Combine(AppPaths.Cache, "covers");

    public static bool Applies(MediaItem item) => (item is PinnedCover c ? c.Target : item) is Station { IsBuilder: false } or ArtistMix or TagMix or BuiltMix or TagItem;

    public Task<string?> GetAsync(MediaItem item)
    {
        if (item is PinnedCover cover) item = cover.Target;
        if (!Applies(item)) return Task.FromResult<string?>(null);
        if (ShapeCovers.Applies(item)) return _shapes.GetAsync(item);
        var (name, label) = Text(item);
        var mix = (item as Station)?.StationKind == StationKind.Meloday ? _meloday.Current : null;
        var key = $"{item.RatingKey}|{name}|{label}|{mix?.Period.Name}|{mix?.PeriodStart:O}|v{Version}";
        var path = Path.Combine(Folder, $"cover-{Hash(key)}.png");
        if (File.Exists(path)) return Task.FromResult<string?>(path);
        var tint = TagTint(item);
        var portrait = item is Station { StationKind: StationKind.Genre or StationKind.Style or StationKind.Mood or StationKind.Decade } station
            ? station.Thumb is { } thumb ? Task.FromResult<string?>(thumb) : App.Services.GetRequiredService<StationService>().GetCoverAsync(station)
            : Task.FromResult<string?>(null);
        return _loads.GetOrAdd(path, _ => DrawAsync(item, name, label, path, mix, tint, portrait)).ContinueWith(t =>
        {
            _loads.TryRemove(path, out _);
            return t.IsCompletedSuccessfully ? t.Result : null;
        }, TaskScheduler.Default);
    }

    private static (string Name, string Label) Text(MediaItem item) => item switch
    {
        Station { StationKind: StationKind.Meloday } => ("Meloday", "Meloday"),
        Station s => (Helpers.Format.StationCoverTitle(s), Helpers.Format.StationCoverLabel(s)),
        BuiltMix { RatingKey: not "mix:discovery" } => (string.Empty, item.Title.EndsWith(" Radio", StringComparison.Ordinal) ? item.Title[..^6] : item.Title),
        ArtistMix or TagMix or BuiltMix => (item.Title.EndsWith(" Mix", StringComparison.Ordinal) ? item.Title[..^4] : item.Title, "Mix"),
        TagItem t => (t.Title, t.TagKind.ToString()),
        _ => (item.Title, string.Empty),
    };

    private static uint TagTint(MediaItem item)
    {
        var c = Helpers.TagColors.ColorFor(item is TagMix t ? t.Tag : item.Title);
        return (uint)(c.R << 16 | c.G << 8 | c.B);
    }

    private async Task<string?> DrawAsync(MediaItem item, string name, string label, string path, MelodayMix? mix, uint tint, Task<string?> portrait)
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
            string? bottomTitle = null;
            uint? ground = null;
            using (var ds = target.CreateDrawingSession())
            {
                if (item is Station station)
                {
                    if (station.StationKind is StationKind.Genre or StationKind.Style or StationKind.Mood or StationKind.Decade)
                    {
                        var photo = await portrait.ConfigureAwait(false);
                        var dominant = await _artwork.GetDominantColorAsync(photo).ConfigureAwait(false);
                        ground = dominant is { } found ? MelodayPalette.Nearest(found) : StationColours.Family(station).Original;
                        ds.Clear(Rgb(ground.Value));
                        await DrawCircleAsync(device, ds, photo, 500, 500, 475).ConfigureAwait(false);
                        PortraitRingCover.Draw(ds, ground.Value);
                    }
                    else
                    {
                        ground = StationColours.Ground(station);
                        WaveField.Draw(ds, size, size, StationWave.Phase(0.8), ground.Value);
                    }
                }
                else if (item is ArtistMix or TagMix or BuiltMix && await MixCoverData.LoadAsync(item, tint).ConfigureAwait(false) is { } mixCover)
                {
                    ds.Clear(Rgb(mixCover.Colour));
                    ground = mixCover.Colour;
                    var artists = new[] { mixCover.Main }.Concat(mixCover.Orbit).ToList();
                    for (var i = 0; i < artists.Count; i++)
                    {
                        var (x, y, d) = MixCircles.StillSlots[i];
                        if (i > 0) ds.FillCircle((float)(x * size), (float)(y * size), (float)((d / 2 + MixCircles.RingShare) * size), Rgb(mixCover.Colour));
                        await DrawCircleAsync(device, ds, artists[i].Thumb, (float)(x * size), (float)(y * size), (float)(d * size / 2)).ConfigureAwait(false);
                    }
                    bottomTitle = mixCover.Info.Title;
                }
                else
                {
                    ground = await ColourAsync(item, tint).ConfigureAwait(false);
                    ds.Clear(Rgb(ground.Value));
                }
            }
            ground ??= CoverSymbol.Dominant(target);
            using (var ds = target.CreateDrawingSession())
            {
                var baseline = bottomTitle is not null
                    ? MelodayCover.DrawTitleBottomLeft(ds, bottomTitle, size, _log)
                    : MelodayCover.DrawLabels(ds, name, label, size, size, _log);
                if (item is not TagItem) CoverEdges.Draw(ds, ground.Value, size, baseline);
            }
            await CoverSymbol.DrawAsync(target, badge: item is Station live ? Helpers.Format.StationBadge(live) : null);
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
            return path;
        }
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

    private async Task<uint> ColourAsync(MediaItem item, uint tint)
    {
        uint? baseColour = null;
        if (item is ArtistMix mix) baseColour = await _artwork.GetDominantColorAsync(mix.Artist.Thumb).ConfigureAwait(false);
        return MelodayPalette.ForWhiteText(baseColour ?? tint);
    }

    private static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
}
