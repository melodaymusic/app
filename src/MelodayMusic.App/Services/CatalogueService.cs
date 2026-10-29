using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.Discovery;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class CatalogueService
{
    private readonly SettingsStore _settings;
    private readonly MusicBrainzClient _client;
    private readonly ILogger<CatalogueService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _enabled = new();
    private readonly string _directory = Path.Combine(AppPaths.Cache, "musicbrainz");
    public event EventHandler? Changed;
    public bool IsOn => _settings.Current.MusicBrainzCatalogue;

    public CatalogueService(SettingsStore settings, HttpClient http, ILogger<CatalogueService> log)
    {
        _settings = settings; _client = new(http, App.Version); _log = log;
    }

    public void SetEnabled(bool on)
    {
        _settings.Current.MusicBrainzCatalogue = on; _settings.Save();
        _enabled.Cancel(); _enabled.Dispose(); _enabled = new();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<CatalogueRelease>> GetAsync(Artist artist, CancellationToken ct)
    {
        if (!IsOn) return [];
        using var enabled = CancellationTokenSource.CreateLinkedTokenSource(ct, _enabled.Token);
        await _gate.WaitAsync(enabled.Token);
        try
        {
            var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(artist.MusicBrainzId ?? artist.Title)));
            var path = Path.Combine(_directory, cacheKey + ".json");
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(1))
            {
                try { return JsonSerializer.Deserialize<List<CatalogueRelease>>(await File.ReadAllTextAsync(path, enabled.Token)) ?? []; }
                catch (JsonException) { }
            }
            var id = Guid.TryParse(artist.MusicBrainzId, out var mbid) ? mbid.ToString() : await _client.FindArtistAsync(artist.Title, enabled.Token);
            if (id is null) return [];
            var releases = await _client.ReleasesAsync(id, enabled.Token);
            enabled.Token.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(_directory);
                await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(releases), enabled.Token);
                File.Move(path + ".tmp", path, true);
            }
            catch (IOException ex) { _log.LogDebug(ex, "MusicBrainz cache could not be saved"); }
            return releases;
        }
        finally { _gate.Release(); }
    }
}
