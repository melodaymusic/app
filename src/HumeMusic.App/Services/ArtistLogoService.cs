using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HumeMusic.Core;
using HumeMusic.Core.Logos;
using HumeMusic.Core.Models;
using HumeMusic.Core.Security;
using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace HumeMusic.Services;

/// <summary>An artist logo ready to show: a PNG trimmed to the logo, and its shape.</summary>
public sealed record PreparedLogo(string Path, double Aspect);

/// <summary>
/// Artist logos. By default each artist gets fanart.tv's best logo, found by MusicBrainz id with the
/// listener's own API key (kept DPAPI-encrypted, never logged). The listener can pick another of
/// fanart.tv's logos, use a logo of their own, or keep the name. Each logo is prepared once (trimmed
/// to its edges, colours kept so it can be shown as made or painted by Colour Drop) and cached on
/// disk. Artists without a logo on fanart.tv are asked about again after a week.
/// </summary>
public sealed class ArtistLogoService
{
    private static readonly TimeSpan RetryMissingAfter = TimeSpan.FromDays(7);
    private const uint MaxWidth = 1000;

    // A known artist (The Beatles) for checking a key.
    private const string ProbeArtist = "b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d";

    private sealed class Entry
    {
        public string? File { get; set; }
        public double Aspect { get; set; }
        public DateTimeOffset CheckedAt { get; set; }
        /// <summary>The fanart.tv address of the chosen logo (null for the listener's own file).</summary>
        public string? Source { get; set; }
        /// <summary>The listener chose this logo (or chose the name) themselves; it is never replaced.</summary>
        public bool Chosen { get; set; }
        /// <summary>The listener prefers the name for this artist.</summary>
        public bool UseName { get; set; }
    }

    private readonly ISecretStore _key;
    private readonly SettingsStore _settings;
    private readonly PlexLogoService _plexLogos;
    private readonly ILogger _log;
    private readonly FanartTv _fanart;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, Task<PreparedLogo?>> _loads = new();
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private Dictionary<string, Entry>? _index;
    private string? _cachedKey;

    public ArtistLogoService(ISecretStore key, SettingsStore settings, PlexLogoService plexLogos, ILogger<ArtistLogoService> log)
    {
        _plexLogos = plexLogos;
        _key = key;
        _settings = settings;
        _log = log;
        // A client of its own: fanart.tv requests carry the key in the address, and never share Plex's client.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MelodayMusic/1.0");
        _fanart = new FanartTv(_http);
    }

    /// <summary>The key, a setting or an artist's logo changed: artist pages should look again.</summary>
    public event EventHandler? Changed;

    public bool HasKey => _key.HasSecret;

    /// <summary>Artist pages show logos (from fanart.tv with a key, or the listener's own).</summary>
    public bool IsEnabled => _settings.Current.ShowArtistLogos;

    /// <summary>Logos are painted by Colour Drop in the page's colour, rather than shown as made.</summary>
    public bool UsesColourDrop => _settings.Current.LogoColours == LogoColours.ColourDrop;

    /// <summary>The key a logo is kept under: the MusicBrainz id, or the artist's Plex key without one.</summary>
    public static string KeyFor(Artist artist) => artist.MusicBrainzId ?? "plex-" + artist.RatingKey;

    // ------------------------------------------------------------------ settings

    /// <summary>Checks a key against fanart.tv and, when it works, stores it securely.</summary>
    public async Task<FanartResult> SaveKeyAsync(string key)
    {
        key = key.Trim();
        var (result, _) = await _fanart.GetLogosAsync(ProbeArtist, key).ConfigureAwait(false);
        if (result is FanartResult.InvalidKey or FanartResult.Unavailable) return result;
        _key.Save(key);
        _cachedKey = key;
        // Artists that had no logo may have been missed because of a bad key.
        await UpdateIndexAsync(index =>
        {
            foreach (var k in index.Where(e => e.Value is { File: null, Chosen: false }).Select(e => e.Key).ToList()) index.Remove(k);
        }).ConfigureAwait(false);
        _loads.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        return FanartResult.Found;
    }

    public void RemoveKey()
    {
        _key.Clear();
        _cachedKey = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetEnabled(bool on)
    {
        _settings.Current.ShowArtistLogos = on;
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetColours(LogoColours colours)
    {
        _settings.Current.LogoColours = colours;
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ an artist's logo

    /// <summary>The artist's logo, prepared for display, or null (logos off, the name chosen, or no logo).</summary>
    public Task<PreparedLogo?> GetAsync(Artist artist)
    {
        if (!IsEnabled) return Task.FromResult<PreparedLogo?>(null);
        var key = KeyFor(artist);
        // The logo on the Plex server is part of what is remembered: set a new one there and the artist shows it.
        var memo = key + "|" + artist.Logo;
        var load = _loads.GetOrAdd(memo, _ => LoadAsync(artist, key));
        // A failed attempt is tried again on the next visit.
        if (load.IsCompleted && load.Result is null) _loads.TryRemove(memo, out _);
        return load;
    }

    private async Task<PreparedLogo?> LoadAsync(Artist artist, string key)
    {
        var index = await IndexAsync().ConfigureAwait(false);
        if (index.TryGetValue(key, out var known))
        {
            if (known.UseName) return null;
            // A logo the listener chose here (fanart.tv's, or their own picture) comes first; otherwise the one the Plex server keeps.
            if (!known.Chosen && artist.Logo is not null && await _plexLogos.GetAsync(artist).ConfigureAwait(false) is { } onServer) return onServer;
            if (known.File is { } f && File.Exists(Path.Combine(AppPaths.LogoCache, f))) return new PreparedLogo(Path.Combine(AppPaths.LogoCache, f), known.Aspect);
            if (known.File is null && DateTimeOffset.UtcNow - known.CheckedAt < RetryMissingAfter) return null;
        }
        // Nothing chosen and nothing remembered: the logo the Plex server keeps for the artist, before fanart.tv's.
        if (!index.ContainsKey(key) && artist.Logo is not null && await _plexLogos.GetAsync(artist).ConfigureAwait(false) is { } fromServer) return fromServer;
        if (artist.MusicBrainzId is not { } mbid) return null;

        var (result, logos) = await OptionsCoreAsync(mbid).ConfigureAwait(false);
        if (result is FanartResult.InvalidKey or FanartResult.Unavailable) return null;
        foreach (var logo in logos.Take(3))
        {
            if (await DownloadAsync(logo.Url, key, artist.Title).ConfigureAwait(false) is not { } prepared) continue;
            await RememberAsync(key, new Entry { File = Path.GetFileName(prepared.Path), Aspect = prepared.Aspect, Source = logo.Url, CheckedAt = DateTimeOffset.UtcNow }).ConfigureAwait(false);
            return prepared;
        }
        await RememberAsync(key, new Entry { CheckedAt = DateTimeOffset.UtcNow }).ConfigureAwait(false);
        return null;
    }

    /// <summary>fanart.tv's logos for an artist, best first, and which one is in use.</summary>
    public async Task<(FanartResult Result, IReadOnlyList<FanartLogo> Logos, string? Current)> GetOptionsAsync(Artist artist)
    {
        var index = await IndexAsync().ConfigureAwait(false);
        var current = index.TryGetValue(KeyFor(artist), out var e) && !e.UseName ? e.Source : null;
        if (artist.MusicBrainzId is not { } mbid) return (FanartResult.NotFound, [], current);
        var (result, logos) = await OptionsCoreAsync(mbid).ConfigureAwait(false);
        return (result, logos, current);
    }

    private async Task<(FanartResult, IReadOnlyList<FanartLogo>)> OptionsCoreAsync(string mbid)
    {
        var apiKey = _cachedKey ??= _key.Load();
        if (apiKey is null) return (FanartResult.InvalidKey, []);
        var (result, logos) = await _fanart.GetLogosAsync(mbid, apiKey).ConfigureAwait(false);
        if (result == FanartResult.InvalidKey) _log.LogWarning("fanart.tv refused the API key; artist logos are paused until it is replaced");
        return (result, logos);
    }

    /// <summary>Uses one of fanart.tv's logos for the artist from now on.</summary>
    public async Task<bool> ChooseAsync(Artist artist, FanartLogo logo)
    {
        var key = KeyFor(artist);
        if (await DownloadAsync(logo.Url, key, artist.Title).ConfigureAwait(false) is not { } prepared) return false;
        await ReplaceAsync(key, new Entry { File = Path.GetFileName(prepared.Path), Aspect = prepared.Aspect, Source = logo.Url, Chosen = true, CheckedAt = DateTimeOffset.UtcNow }).ConfigureAwait(false);
        return true;
    }

    /// <summary>Uses the listener's own image (PNG, WebP or JPEG) as the artist's logo.</summary>
    public async Task<bool> UseOwnAsync(Artist artist, byte[] image)
    {
        var key = KeyFor(artist);
        var prepared = await PrepareAsync(image, key, "own-" + DateTime.UtcNow.Ticks, requireTransparency: false).ConfigureAwait(false);
        if (prepared is null) return false;
        await ReplaceAsync(key, new Entry { File = Path.GetFileName(prepared.Path), Aspect = prepared.Aspect, Chosen = true, CheckedAt = DateTimeOffset.UtcNow }).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Uses the listener's own logo files for many artists at once (from "Import Logos"). Each file is
    /// prepared into the app's logo cache, so the originals can be moved or deleted afterwards. Returns
    /// how many were set.
    /// </summary>
    public async Task<int> ImportAsync(IReadOnlyList<(Artist Artist, LogoFile Logo)> logos, IProgress<int>? progress = null)
    {
        var prepared = new List<(string Key, Entry Entry)>();
        foreach (var (artist, logo) in logos)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(logo.Path).ConfigureAwait(false);
                var key = KeyFor(artist);
                if (await PrepareAsync(bytes, key, "own-" + logo.Path, requireTransparency: false).ConfigureAwait(false) is { } ready)
                    prepared.Add((key, new Entry { File = Path.GetFileName(ready.Path), Aspect = ready.Aspect, Chosen = true, CheckedAt = DateTimeOffset.UtcNow }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                _log.LogInformation("The logo file for {Artist} couldn't be used: {Message}", artist.Title, ex.Message);
            }
            progress?.Report(prepared.Count);
        }

        // Logos they replace stay in the cache (they're small), so an import never removes files.
        await UpdateIndexAsync(index =>
        {
            foreach (var (key, entry) in prepared) index[key] = entry;
        }).ConfigureAwait(false);
        _loads.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        _log.LogInformation("Imported {Count} of {Total} artist logos", prepared.Count, logos.Count);
        return prepared.Count;
    }

    /// <summary>Shows the artist's name rather than a logo.</summary>
    public Task UseNameAsync(Artist artist) =>
        ReplaceAsync(KeyFor(artist), new Entry { UseName = true, Chosen = true, CheckedAt = DateTimeOffset.UtcNow });

    /// <summary>Back to fanart.tv's best logo.</summary>
    public Task ResetAsync(Artist artist) => ReplaceAsync(KeyFor(artist), null);

    private async Task ReplaceAsync(string key, Entry? entry)
    {
        string? replaced = null;
        await UpdateIndexAsync(index =>
        {
            if (index.TryGetValue(key, out var old) && old.File != entry?.File) replaced = old.File;
            if (entry is null) index.Remove(key);
            else index[key] = entry;
        }).ConfigureAwait(false);
        // The app's own cached copy of a logo it no longer uses.
        if (replaced is not null)
        {
            try { File.Delete(Path.Combine(AppPaths.LogoCache, replaced)); }
            catch (IOException) { }
        }
        _loads.TryRemove(key, out _);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<PreparedLogo?> DownloadAsync(string url, string key, string title)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            return await PrepareAsync(bytes, key, url, requireTransparency: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            _log.LogInformation("A logo for {Artist} couldn't be prepared: {Message}", title, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Trims the logo to its visible edges, keeping its colours (Colour Drop paints by its shape alone).
    /// A fanart.tv image without transparency is a picture, not a logo, and is skipped.
    /// </summary>
    internal static async Task<PreparedLogo?> PrepareAsync(byte[] bytes, string key, string source, bool requireTransparency)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(bytes.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        var scale = decoder.PixelWidth > MaxWidth ? MaxWidth / (double)decoder.PixelWidth : 1;
        var width = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
        var height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
        var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = data.DetachPixelData();

        int left = (int)width, top = (int)height, right = -1, bottom = -1, transparent = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var a = pixels[(y * width + x) * 4 + 3];
            if (a < 250) transparent++;
            if (a <= 12) continue;
            if (x < left) left = x;
            if (x > right) right = x;
            if (y < top) top = y;
            if (y > bottom) bottom = y;
        }
        if (right < left || bottom < top) return null;
        if (requireTransparency && transparent < width * height / 20) return null;

        var w = right - left + 1;
        var h = bottom - top + 1;
        var output = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            Array.Copy(pixels, ((top + y) * (int)width + left) * 4, output, y * w * 4, w * 4);

        Directory.CreateDirectory(AppPaths.LogoCache);
        // A name of its own for each source, so a newly chosen logo is never mistaken for the old one.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(source + bytes.Length)))[..10].ToLowerInvariant();
        var path = Path.Combine(AppPaths.LogoCache, $"{key}-{hash}.png");
        using (var file = new InMemoryRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)w, (uint)h, 96, 96, output);
            await encoder.FlushAsync();
            file.Seek(0);
            var buffer = new byte[file.Size];
            await file.ReadAsync(buffer.AsBuffer(), (uint)file.Size, InputStreamOptions.None);
            await File.WriteAllBytesAsync(path, buffer);
        }
        return new PreparedLogo(path, w / (double)h);
    }

    // ------------------------------------------------------------------ cache index

    // Version 2: logos keep their colours (version 1 kept only their shape).
    private static string IndexPath => Path.Combine(AppPaths.LogoCache, "index-v2.json");

    private async Task<Dictionary<string, Entry>> IndexAsync()
    {
        if (_index is { } ready) return ready;
        await _indexGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_index is null)
            {
                try
                {
                    _index = File.Exists(IndexPath)
                        ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(await File.ReadAllTextAsync(IndexPath).ConfigureAwait(false)) ?? []
                        : [];
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    _index = [];
                }
            }
            return _index;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private Task RememberAsync(string key, Entry entry) => UpdateIndexAsync(index =>
    {
        // Never overwrite the listener's own choice with an automatic one.
        if (index.TryGetValue(key, out var existing) && existing.Chosen) return;
        index[key] = entry;
    });

    private async Task UpdateIndexAsync(Action<Dictionary<string, Entry>> change)
    {
        var index = await IndexAsync().ConfigureAwait(false);
        await _indexGate.WaitAsync().ConfigureAwait(false);
        try
        {
            change(index);
            Directory.CreateDirectory(AppPaths.LogoCache);
            await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(index)).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The index is only a shortcut; logos are looked up again without it.
        }
        finally
        {
            _indexGate.Release();
        }
    }
}
