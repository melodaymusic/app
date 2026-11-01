using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using MelodayMusic.Core.Taste;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class TasteService : IDisposable
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);
    private const int HistoryMax = 12_000;
    private const int AlbumLookups = 600;
    private static readonly TimeSpan SoundCheckEvery = TimeSpan.FromMinutes(5);

    private readonly PlexSession _session;
    private readonly TasteMemory _memory;
    private readonly SettingsStore _settings;
    private readonly Core.Home.RecentStore _recents;
    private readonly PlayerService _player;
    private readonly MelodayEngine _melodayEngine;
    private readonly MusicIntelligence _intelligence;
    private readonly Exposure _exposure;
    private readonly TasteExclusions _exclusions;
    private readonly ILogger<TasteService> _log;
    private readonly Dictionary<string, AlbumTags> _albumTags = new(StringComparer.Ordinal);
    private readonly Timer _timer;
    private readonly Timer _soundTimer;
    private readonly SemaphoreSlim _building = new(1, 1);
    private volatile TasteEngine _engine;
    private DateTime _chosenAt = DateTime.MinValue;
    private string? _library;
    private int _soundFrom;

    public TasteService(PlexSession session, TasteMemory memory, SettingsStore settings, Core.Home.RecentStore recents, PlayerService player,
        MelodayEngine melodayEngine, MusicIntelligence intelligence, Exposure exposure, TasteExclusions exclusions, ILogger<TasteService> log)
    {
        _exposure = exposure;
        _exclusions = exclusions;
        _session = session;
        _memory = memory;
        _settings = settings;
        _recents = recents;
        _player = player;
        _melodayEngine = melodayEngine;
        _intelligence = intelligence;
        _log = log;
        _engine = new TasteEngine(memory: memory, adventure: Adventure, exposure: exposure);
        _exclusions.Changed += (_, _) => _ = RefreshAsync();
        _session.StateChanged += (_, _) => _ = RefreshAsync();
        _recents.Changed += (_, _) => _chosenAt = DateTime.UtcNow;
        _player.QueueReplaced += OnQueueReplaced;
        _memory.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _intelligence.Changed += (_, _) => _ = RefreshSoundAsync();
        _timer = new Timer(_ => _ = RefreshAsync(), null, RefreshEvery, RefreshEvery);
        _soundTimer = new Timer(_ => { if (SoundIsStale) _ = RefreshSoundAsync(); }, null, SoundCheckEvery, SoundCheckEvery);
        _ = RefreshAsync();
    }

    public TasteEngine Engine => _engine;
    public TasteMemory Memory => _memory;
    public Exposure Exposure => _exposure;
    public TasteExclusions Exclusions => _exclusions;

    /// <summary>Notes what a feature has just suggested, so the same songs and artists aren't put forward everywhere at once.</summary>
    public void Shown(IEnumerable<Track> tracks) => _exposure.Shown(tracks.Select(t => (t.RatingKey, t.ArtistRatingKey)));

    public event EventHandler? Changed;

    public double Adventure => Math.Clamp(_settings.Current.TasteAdventure, 0, 1);

    public void SetAdventure(double value)
    {
        _settings.Current.TasteAdventure = Math.Clamp(value, 0, 1);
        _settings.Save();
        _engine = new TasteEngine(_engine.Profile, _memory, Adventure, _engine.Sound, _exposure);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Forget() => _memory.Clear();

    public async Task RefreshAsync()
    {
        if (!_session.IsReady || _session.Client is not { } client || _session.Section is not { } section) return;
        if (!await _building.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            var library = $"{_session.Server?.MachineIdentifier}/{section.Key}";
            if (library != _library) _albumTags.Clear();
            _library = library;

            var now = DateTimeOffset.Now;
            var account = _session.Server?.Owned == true ? "1" : null;
            var heard = await client.GetHistoryAsync(section.Key, now.AddDays(-TasteProfile.LookbackDays), account, HistoryMax).ConfigureAwait(false);
            // Plays from a playlist, album, mix or station marked "Don't use for my taste profile" don't shape taste.
            var plays = heard.Where(p => !_exclusions.IsUntasted(p.RatingKey, p.ViewedAt)).ToList();

            var wanted = plays.Where(p => p.AlbumKey is not null).GroupBy(p => p.AlbumKey!)
                .OrderByDescending(g => g.Sum(p => TasteProfile.Weight(p.ViewedAt, now)))
                .Select(g => g.Key).Where(k => !_albumTags.ContainsKey(k)).Take(AlbumLookups).ToList();
            if (wanted.Count > 0)
            {
                foreach (var album in (await client.GetItemsAsync(wanted).ConfigureAwait(false)).OfType<Album>())
                    _albumTags[album.RatingKey] = new AlbumTags(
                        album.Genres.Concat(album.Styles).ToHashSet(StringComparer.OrdinalIgnoreCase),
                        album.Moods.ToHashSet(StringComparer.OrdinalIgnoreCase), album.Year);
            }

            var profile = await Task.Run(() => TasteProfile.Build(plays, k => _albumTags.GetValueOrDefault(k), now)).ConfigureAwait(false);
            var sound = await HearAsync(profile).ConfigureAwait(false);
            _engine = new TasteEngine(profile, _memory, Adventure, sound, _exposure);
            _log.LogInformation("Taste Engine: {Plays} Plex plays, {Albums} albums' tags, {Remembered} songs remembered here, {Sounds} sounds from {Songs} songs",
                profile.Plays, _albumTags.Count, _memory.Songs, sound.Clusters, sound.Songs);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogInformation("Taste Engine: Plex history unavailable ({Message})", ex.Message);
        }
        finally
        {
            _building.Release();
        }
    }

    private bool SoundIsStale => _intelligence.IsMeloday && _melodayEngine.Count is var count && count > 0
        && count >= Math.Max(_soundFrom * 1.1, _soundFrom + 200);

    private async Task RefreshSoundAsync()
    {
        if (!await _building.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            var current = _engine;
            var sound = await HearAsync(current.Profile).ConfigureAwait(false);
            if (ReferenceEquals(sound, current.Sound)) return;
            _engine = new TasteEngine(current.Profile, _memory, Adventure, sound, _exposure);
            _log.LogInformation("Taste Engine: {Sounds} sounds from {Songs} songs", sound.Clusters, sound.Songs);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _building.Release();
        }
    }

    private async Task<SoundTaste> HearAsync(TasteProfile profile)
    {
        if (!_intelligence.IsMeloday || _melodayEngine.Count == 0)
        {
            _soundFrom = 0;
            return SoundTaste.Empty;
        }
        return await Task.Run(() =>
        {
            var thread = Thread.CurrentThread;
            var priority = thread.Priority;
            try
            {
                thread.Priority = ThreadPriority.BelowNormal;
                var fingerprints = _melodayEngine.Snapshot().ToDictionary(p => p.Key, p => p.Value.Vector, StringComparer.Ordinal);
                _soundFrom = fingerprints.Count;
                return SoundTaste.Build(fingerprints, SoundTaste.Samples(profile, _memory));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogWarning(ex, "Taste Engine: sound unavailable");
                return SoundTaste.Empty;
            }
            finally { thread.Priority = priority; }
        }).ConfigureAwait(false);
    }

    private void OnQueueReplaced(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _chosenAt < TimeSpan.FromSeconds(30) || _player.IsLive || _player.Queue.IsShuffled) return;
        if (_player.Queue.Current?.Track is { } picked && !_player.IsUntasted(picked)) _memory.Record(TasteSignal.Pick, picked.RatingKey, picked.ArtistRatingKey);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _soundTimer.Dispose();
        _building.Dispose();
    }
}
