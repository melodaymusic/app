using System.Numerics;
using System.Text.RegularExpressions;
using MelodayMusic.Core;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Covers;
using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playlists;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Svg;
using Windows.Foundation;
using Windows.UI;

namespace MelodayMusic.Services;

public sealed record ShapeLayer(string File, double X, double Y, double Side, bool Round);

public sealed record ShapeCoverLayers(uint Ground, string Overlay, IReadOnlyList<ShapeLayer> Shapes);

public sealed partial class ShapeCovers
{
    private const int Version = 8;
    private const float ViewBox = 480;
    private const int MaskCells = 48;

    private readonly ILogger<ShapeCovers> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<string?>> _loads = new();
    private IReadOnlyList<(string Svg, ShapeMask Mask)>? _shapes;

    public ShapeCovers(ILogger<ShapeCovers> log) => _log = log;

    private static string Folder => Path.Combine(AppPaths.Cache, "covers", "shapes");
    private static string ShapesFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "Shapes");

    public static bool IsArtistBased(MediaItem item) => item switch
    {
        ArtistMix => true,
        NicheMix niche => niche.ArtistNames.Count >= 2,
        TagMix tag => NicheMixRecipe.TryFrom(tag, out var recipe) && recipe.Artists.Count >= 2,
        BuiltMix built => built.RatingKey != "mix:discovery" && built.ArtistKeys.Count > 0,
        _ => false,
    };

    public static bool Applies(MediaItem? item) => item is TagMix or BuiltMix && !IsArtistBased(item);

    public static uint Ground(MediaItem mix)
    {
        var key = mix.RatingKey;
        var families = MelodayPalette.Families.OrderBy(f => MadePlaylists.Stable(f.Name)).ToList();
        var family = families[MadePlaylists.Stable(key) % families.Count];
        var readable = family.Shades.Where(c => DominantColor.Luminance(c) <= 0.55).ToList();
        return readable.Count == 0 ? family.Darker : readable[MadePlaylists.Stable(key + "|shade") % readable.Count];
    }

    public Task<string?> GetAsync(MediaItem mix)
    {
        if (!Applies(mix)) return Task.FromResult<string?>(null);
        var ground = Ground(mix);
        var path = Path.Combine(Folder, $"{MadePlaylists.Stable(mix.RatingKey + "|" + mix.Title):x8}-{ground:x6}-v{Version}.png");
        if (File.Exists(path) && File.Exists(LayersFile(path))) return Task.FromResult<string?>(path);
        return _loads.GetOrAdd(path, _ => DrawAsync(mix, ground, path));
    }

    public async Task<ShapeCoverLayers?> GetLayersAsync(MediaItem mix)
    {
        if (await GetAsync(mix).ConfigureAwait(false) is not { } path) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ShapeCoverLayers>(await File.ReadAllTextAsync(LayersFile(path)).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string LayersFile(string path) => Path.ChangeExtension(path, ".layers.json");

    private async Task<string?> DrawAsync(MediaItem mix, uint ground, string path)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) return path;
            var device = CanvasDevice.GetSharedDevice();
            var shapes = _shapes ??= LoadShapes(device);
            if (shapes.Count < 2) return null;
            var layout = await Task.Run(() => ShapeLayout.Arrange(shapes.Select(s => s.Mask).ToList(), MadePlaylists.Stable(mix.RatingKey), ground))
                .ConfigureAwait(false);

            Directory.CreateDirectory(Folder);
            const float size = 1000;
            var shapeFiles = new List<ShapeLayer>();
            foreach (var (placement, colour, part) in new[] { (layout.Back, layout.BackColour, "back"), (layout.Front, layout.FrontColour, "front") })
            {
                var side = (float)Math.Max(1, Math.Round(placement.Side * size));
                using var alone = new CanvasRenderTarget(device, side, side, 96);
                using (var ds = alone.CreateDrawingSession())
                {
                    ds.Clear(Colors.Transparent);
                    using var svg = CanvasSvgDocument.LoadFromXml(device, Tinted(shapes[placement.Shape].Svg, colour));
                    ds.Transform = Transform(placement with { X = 0, Y = 0 }, size);
                    ds.DrawSvg(svg, new Size(ViewBox, ViewBox));
                }
                var file = Path.ChangeExtension(path, $".{part}.png");
                await alone.SaveAsync(file, CanvasBitmapFileFormat.Png);
                shapeFiles.Add(new ShapeLayer(file, placement.X, placement.Y, placement.Side, ShapeLayout.IsRound(shapes[placement.Shape].Mask)));
            }
            using var target = new CanvasRenderTarget(device, size, size, 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Rgb(ground));
                foreach (var (placement, colour) in new[] { (layout.Back, layout.BackColour), (layout.Front, layout.FrontColour) })
                {
                    using var svg = CanvasSvgDocument.LoadFromXml(device, Tinted(shapes[placement.Shape].Svg, colour));
                    ds.Transform = Transform(placement, size);
                    ds.DrawSvg(svg, new Size(ViewBox, ViewBox));
                }
                ds.Transform = Matrix3x2.Identity;
                var baseline = MelodayCover.DrawTitleBottomLeft(ds, mix.Title, size, _log);
                CoverEdges.Draw(ds, ground, size, baseline);
            }
            var ink = CoverSymbol.InkFor(target);
            await CoverSymbol.DrawAsync(target, ink);
            await target.SaveAsync(path, CanvasBitmapFileFormat.Png);

            using var over = new CanvasRenderTarget(device, size, size, 96);
            using (var ds = over.CreateDrawingSession())
            {
                ds.Clear(Colors.Transparent);
                var baseline = MelodayCover.DrawTitleBottomLeft(ds, mix.Title, size, _log);
                CoverEdges.Draw(ds, ground, size, baseline);
            }
            await CoverSymbol.DrawAsync(over, ink);
            var overFile = Path.ChangeExtension(path, ".over.png");
            await over.SaveAsync(overFile, CanvasBitmapFileFormat.Png);
            await File.WriteAllTextAsync(LayersFile(path), System.Text.Json.JsonSerializer.Serialize(new ShapeCoverLayers(ground, overFile, shapeFiles)));
            return path;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "The cover for {Mix} couldn't be drawn", mix.Title);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static Matrix3x2 Transform(ShapePlacement at, float size)
    {
        var side = (float)(at.Side * size);
        var half = side / 2;
        return Matrix3x2.CreateScale(side / ViewBox)
            * Matrix3x2.CreateRotation((float)(at.Turns * Math.PI / 2), new Vector2(half, half))
            * Matrix3x2.CreateTranslation((float)(at.X * size), (float)(at.Y * size));
    }

    private List<(string Svg, ShapeMask Mask)> LoadShapes(CanvasDevice device)
    {
        var shapes = new List<(string, ShapeMask)>();
        if (!Directory.Exists(ShapesFolder)) return shapes;
        foreach (var file in Directory.EnumerateFiles(ShapesFolder, "*.svg").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var svg = File.ReadAllText(file).Trim('﻿', ' ', '\r', '\n', '\t');
                using var document = CanvasSvgDocument.LoadFromXml(device, Tinted(svg, 0xFFFFFF));
                using var target = new CanvasRenderTarget(device, MaskCells, MaskCells, 96);
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(Colors.Transparent);
                    ds.DrawSvg(document, new Size(MaskCells, MaskCells));
                }
                var pixels = target.GetPixelBytes();
                var cells = new bool[MaskCells * MaskCells];
                for (var i = 0; i < cells.Length; i++) cells[i] = pixels[i * 4 + 3] >= 128;
                var mask = new ShapeMask(MaskCells, cells);
                if (mask.Area > 0) shapes.Add((svg, mask));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "The cover shape {Shape} couldn't be read", Path.GetFileName(file));
            }
        }
        return shapes;
    }

    private static string Tinted(string svg, uint rgb) => FillAttribute().Replace(svg, $"fill='#{rgb & 0xFFFFFF:x6}'");

    [GeneratedRegex("""fill\s*=\s*(['"])[^'"]*\1""")]
    private static partial Regex FillAttribute();

    private static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static class Colors
    {
        public static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);
    }
}
