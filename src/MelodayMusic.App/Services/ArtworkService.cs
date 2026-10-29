using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using MelodayMusic.Core.Services;

namespace MelodayMusic.Services;

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

    public async Task<string?> GetLocalPathAsync(string? plexPath, int pixelSize, CancellationToken ct = default)
    {
        if (plexPath is { Length: > 2 } && plexPath[1] == ':' && File.Exists(plexPath)) return plexPath;
        if (Uri.TryCreate(plexPath, UriKind.Absolute, out var external) && external.Scheme == "https"
            && external.Host == "coverartarchive.org")
            return await _cache.GetAsync("catalogue|" + external.AbsoluteUri, external, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(plexPath) || _session.Client is not { } client) return null;
        var size = Bucket(pixelSize);
        var uri = client.BuildArtworkUri(plexPath, size, size);
        if (uri is null) return null;
        var key = $"{client.Server.MachineIdentifier}|{plexPath}|{size}";
        return await _cache.GetAsync(key, uri, ct).ConfigureAwait(false);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ArtworkPalette?> _ultraBlur = new();

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
        }
        palette ??= await SamplePaletteAsync(plexPath, ct).ConfigureAwait(false);
        _ultraBlur[plexPath] = palette;
        return palette;
    }

    public static ArtworkPalette Readable(ArtworkPalette p) => new(
        MelodayMusic.Core.Artwork.DominantColor.ForBackdrop(p.TopLeft),
        MelodayMusic.Core.Artwork.DominantColor.ForBackdrop(p.TopRight),
        MelodayMusic.Core.Artwork.DominantColor.ForBackdrop(p.BottomRight),
        MelodayMusic.Core.Artwork.DominantColor.ForBackdrop(p.BottomLeft));

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ArtworkPalette?> _edges = new();

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
            palette = MelodayMusic.Core.Artwork.EdgePalette.From(data.DetachPixelData(), (int)width, (int)height);
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(_log, "Edge colours failed for {Path}: {Message}", plexPath, ex.Message);
        }
        _edges[plexPath] = palette;
        return palette;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint?> _fills = new();

    public async Task<uint?> GetPageFillAsync(string? plexPath, bool bottomEdge, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plexPath)) return null;
        var key = (bottomEdge ? "edge|" : "cover|") + plexPath;
        if (_fills.TryGetValue(key, out var cached)) return cached;
        var file = await GetLocalPathAsync(plexPath, 320, ct).ConfigureAwait(false);
        if (file is null) return null;
        uint? colour = null;
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
            var pixels = data.DetachPixelData();
            colour = bottomEdge
                ? MelodayMusic.Core.Artwork.EdgePalette.MostCommon(pixels, (int)width, (int)height, (0, 0.9, 1, 1))
                : MelodayMusic.Core.Artwork.DominantColor.Find(pixels) ?? MelodayMusic.Core.Artwork.EdgePalette.MostCommon(pixels, (int)width, (int)height, (0, 0, 1, 1));
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(_log, "Page fill failed for {Path}: {Message}", plexPath, ex.Message);
        }
        _fills[key] = colour;
        return colour;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint[]?> _glows = new();

    public async Task<uint[]?> GetGlowColoursAsync(string? plexPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plexPath)) return null;
        if (_glows.TryGetValue(plexPath, out var cached)) return cached;
        var file = await GetLocalPathAsync(plexPath, 320, ct).ConfigureAwait(false);
        if (file is null) return null;
        uint[]? colours = null;
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
            colours = MelodayMusic.Core.Artwork.GlowPalette.From(data.DetachPixelData(), (int)width, (int)height)
                ?.Select(MelodayMusic.Core.Artwork.DominantColor.ForBackdrop).ToArray();
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(_log, "Glow colours failed for {Path}: {Message}", plexPath, ex.Message);
        }
        _glows[plexPath] = colours;
        return colours;
    }

    public async Task<string?> GetUltraBlurImageAsync(ArtworkPalette palette, int width, int height, CancellationToken ct = default)
    {
        if (_session.Client is not { } client) return null;
        palette = Readable(palette);
        var uri = client.BuildUltraBlurUri(palette, width, height);
        var key = $"ultrablur-clean|{ArtworkPalette.Hex(palette.TopLeft)}{ArtworkPalette.Hex(palette.TopRight)}" +
                  $"{ArtworkPalette.Hex(palette.BottomRight)}{ArtworkPalette.Hex(palette.BottomLeft)}|{width}x{height}";
        return await _cache.GetAsync(key, uri, ct).ConfigureAwait(false);
    }

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
            return null;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint?> _dominant = new();

    public Task<uint?> GetDominantColorAsync(string? plexPath, CancellationToken ct = default) =>
        AnalyseAsync(_dominant, plexPath, MelodayMusic.Core.Artwork.DominantColor.Find, ct);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint?> _vibrant = new();

    public Task<uint?> GetVibrantColorAsync(string? plexPath, CancellationToken ct = default) =>
        AnalyseAsync(_vibrant, plexPath, MelodayMusic.Core.Artwork.AccentColors.Vibrant, ct);

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
