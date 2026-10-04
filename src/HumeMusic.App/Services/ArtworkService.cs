using HumeMusic.Core.Caching;
using HumeMusic.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using HumeMusic.Core.Services;

namespace HumeMusic.Services;

/// <summary>
/// Resolves Plex artwork paths to locally cached, server-resized image files. Sizes are bucketed
/// so the same artwork shown at slightly different sizes shares one cached file.
/// </summary>
public sealed class ArtworkService
{
    private readonly PlexSession _session;
    private readonly ArtworkCache _cache;

    private readonly Microsoft.Extensions.Logging.ILogger _log;

    public ArtworkService(PlexSession session, ArtworkCache cache, Microsoft.Extensions.Logging.ILogger<ArtworkService> log)
    {
        _session = session;
        _cache = cache;
        _log = log;
    }

    public static int Bucket(int pixels) => pixels switch
    {
        <= 96 => 96,
        <= 200 => 200,
        <= 320 => 320,
        <= 480 => 480,
        <= 720 => 720,
        _ => 1080,
    };

    /// <summary>Returns a local file path for the artwork, or null if none/unavailable.</summary>
    public async Task<string?> GetLocalPathAsync(string? plexPath, int pixelSize, CancellationToken ct = default)
    {
        // A picture the app made itself (a Meloday cover) is already on disk.
        if (plexPath is { Length: > 2 } && plexPath[1] == ':' && File.Exists(plexPath)) return plexPath;
        if (string.IsNullOrWhiteSpace(plexPath) || _session.Client is not { } client) return null;
        var size = Bucket(pixelSize);
        var uri = client.BuildArtworkUri(plexPath, size, size);
        if (uri is null) return null;
        var key = $"{client.Server.MachineIdentifier}|{plexPath}|{size}";
        return await _cache.GetAsync(key, uri, ct).ConfigureAwait(false);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ArtworkPalette?> _ultraBlur = new();

    /// <summary>
    /// UltraBlur colours for an artwork path (cached): Plex's own, or, when the server can't provide
    /// them (its colour service fails for some images), sampled from the image's corners the same way.
    /// </summary>
    public async Task<ArtworkPalette?> GetUltraBlurPaletteAsync(string? plexPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plexPath) || _session.Client is not { } client) return null;
        if (_ultraBlur.TryGetValue(plexPath, out var cached)) return cached;
        ArtworkPalette? palette = null;
        try
        {
            palette = await client.GetUltraBlurColorsAsync(plexPath, ct).ConfigureAwait(false);
        }
        catch (Core.Plex.PlexException)
        {
            // Fall through to sampling the image.
        }
        palette ??= await SamplePaletteAsync(plexPath, ct).ConfigureAwait(false);
        _ultraBlur[plexPath] = palette;
        return palette;
    }

    /// <summary>
    /// UltraBlur colours made safe for white text while staying the colours they are: each keeps its
    /// hue and saturation, and only pale ones are deepened (never swapped for a palette colour).
    /// </summary>
    public static ArtworkPalette Readable(ArtworkPalette p) => new(
        HumeMusic.Core.Artwork.DominantColor.ForBackdrop(p.TopLeft),
        HumeMusic.Core.Artwork.DominantColor.ForBackdrop(p.TopRight),
        HumeMusic.Core.Artwork.DominantColor.ForBackdrop(p.BottomRight),
        HumeMusic.Core.Artwork.DominantColor.ForBackdrop(p.BottomLeft));

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ArtworkPalette?> _edges = new();

    /// <summary>
    /// Backdrop colours for a hero image that sits at the top right and fades out to its left and
    /// bottom: the most common colour where each of its edges meets the page (see EdgePalette). A
    /// small copy of the image is enough, and the answer is cached per image.
    /// </summary>
    public async Task<ArtworkPalette?> GetEdgePaletteAsync(string? plexPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plexPath)) return null;
        if (_edges.TryGetValue(plexPath, out var cached)) return cached;
        var file = await GetLocalPathAsync(plexPath, 320, ct).ConfigureAwait(false);
        if (file is null) return null;
        ArtworkPalette? palette = null;
        try
        {
            var storage = await StorageFile.GetFileFromPathAsync(file);
            using var stream = await storage.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            const uint width = 96;
            var height = (uint)Math.Max(1, Math.Round(width * decoder.PixelHeight / (double)Math.Max(1, decoder.PixelWidth)));
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            palette = HumeMusic.Core.Artwork.EdgePalette.From(data.DetachPixelData(), (int)width, (int)height);
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(_log, "Edge colours failed for {Path}: {Message}", plexPath, ex.Message);
        }
        _edges[plexPath] = palette;
        return palette;
    }

    /// <summary>A local file of the server-rendered UltraBlur background for a palette.</summary>
    public async Task<string?> GetUltraBlurImageAsync(ArtworkPalette palette, int width, int height, CancellationToken ct = default)
    {
        if (_session.Client is not { } client) return null;
        palette = Readable(palette);
        var uri = client.BuildUltraBlurUri(palette, width, height);
        var key = $"ultrablur-clean|{ArtworkPalette.Hex(palette.TopLeft)}{ArtworkPalette.Hex(palette.TopRight)}" +
                  $"{ArtworkPalette.Hex(palette.BottomRight)}{ArtworkPalette.Hex(palette.BottomLeft)}|{width}x{height}";
        return await _cache.GetAsync(key, uri, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Samples artwork colours locally (for items where Plex supplies no palette, such as playlists):
    /// the image is decoded at 4x4 and the top-left, top-right, bottom-right and bottom-left cells used.
    /// </summary>
    public async Task<ArtworkPalette?> SamplePaletteAsync(string? plexPath, CancellationToken ct = default)
    {
        var file = await GetLocalPathAsync(plexPath, 96, ct).ConfigureAwait(false);
        if (file is null) return null;
        try
        {
            var storage = await StorageFile.GetFileFromPathAsync(file);
            using var stream = await storage.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = 4, ScaledHeight = 4, InterpolationMode = BitmapInterpolationMode.Fant },
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var px = data.DetachPixelData();
            uint At(int x, int y)
            {
                var i = (y * 4 + x) * 4;
                return (uint)(px[i + 2] << 16 | px[i + 1] << 8 | px[i]);
            }
            return new ArtworkPalette(At(0, 0), At(3, 0), At(3, 3), At(0, 3));
        }
        catch (Exception)
        {
            // A palette is decoration only; any decoding problem just means no tint.
            return null;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint?> _dominant = new();

    /// <summary>
    /// The most common colour in the artwork (0xRRGGBB): pixels of a 40x40 decode are grouped into
    /// 4-bit-per-channel buckets, the fullest bucket wins and its pixels are averaged.
    /// </summary>
    public Task<uint?> GetDominantColorAsync(string? plexPath, CancellationToken ct = default) =>
        AnalyseAsync(_dominant, plexPath, HumeMusic.Core.Artwork.DominantColor.Find, ct);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint?> _vibrant = new();

    /// <summary>The most vivid colour in the artwork (for the app accent), or null for colourless art.</summary>
    public Task<uint?> GetVibrantColorAsync(string? plexPath, CancellationToken ct = default) =>
        AnalyseAsync(_vibrant, plexPath, HumeMusic.Core.Artwork.AccentColors.Vibrant, ct);

    /// <summary>Runs a colour analysis over a 40x40 decode of the artwork, caching the answer per path.</summary>
    private async Task<uint?> AnalyseAsync(System.Collections.Concurrent.ConcurrentDictionary<string, uint?> cache, string? plexPath,
        Func<byte[], uint?> analyse, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plexPath)) return null;
        if (cache.TryGetValue(plexPath, out var cached)) return cached;
        var file = await GetLocalPathAsync(plexPath, 96, ct).ConfigureAwait(false);
        if (file is null) return null;
        uint? result = null;
        try
        {
            var storage = await StorageFile.GetFileFromPathAsync(file);
            using var stream = await storage.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            const int n = 40;
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = n, ScaledHeight = n, InterpolationMode = BitmapInterpolationMode.Fant },
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            result = analyse(data.DetachPixelData());
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(_log, "Artwork analysis failed for {Path}: {Message}", plexPath, ex.Message);
            result = null;
        }
        cache[plexPath] = result;
        return result;
    }

    public Uri? GetRemoteUri(string? plexPath, int pixelSize) =>
        _session.Client?.BuildArtworkUri(plexPath, Bucket(pixelSize), Bucket(pixelSize));
}
