using System.Collections.Concurrent;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MelodayMusic.Services;

public sealed class DownloadedTrack
{
    public required Track Track { get; init; }
    public required string FileName { get; init; }
    public long Bytes { get; init; }
    public string? ServerId { get; init; }
    public DateTimeOffset DownloadedAt { get; init; }
}

public sealed partial class DownloadService : ObservableObject
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly string _folder = Path.Combine(AppPaths.Shared, "downloads");
    private string IndexPath => Path.Combine(_folder, "index.json");

    private readonly PlexSession _session;
    private readonly HttpClient _http;
    private readonly NotificationService _notifications;
    private readonly ILogger<DownloadService> _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly ConcurrentDictionary<string, DownloadedTrack> _items = new();
    private readonly ConcurrentQueue<Track> _waiting = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private int _running;

    public DownloadService(PlexSession session, HttpClient http, NotificationService notifications, ILogger<DownloadService> log)
    {
        _session = session;
        _http = http;
        _notifications = notifications;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Load();
    }

    public event EventHandler? Changed;

    [ObservableProperty] public partial int Remaining { get; private set; }

    public int Count => _items.Count;
    public long TotalBytes => _items.Values.Sum(i => i.Bytes);

    public IReadOnlyList<Track> Tracks => _items.Values.OrderByDescending(i => i.DownloadedAt).Select(i => i.Track).ToList();

    public bool IsDownloaded(string ratingKey) => _items.ContainsKey(ratingKey);

    public bool IsPending(string ratingKey) => _pending.ContainsKey(ratingKey);

    public Uri? LocalUri(Track track)
    {
        if (!_items.TryGetValue(track.RatingKey, out var item)) return null;
        var path = Path.Combine(_folder, item.FileName);
        return File.Exists(path) ? new Uri(path) : null;
    }

    public void Download(IEnumerable<Track> tracks)
    {
        var added = 0;
        foreach (var track in tracks)
        {
            if (_items.ContainsKey(track.RatingKey) || !_pending.TryAdd(track.RatingKey, 0)) continue;
            _waiting.Enqueue(track);
            added++;
        }
        if (added == 0) return;
        Remaining = _pending.Count;
        if (Interlocked.Exchange(ref _running, 1) == 0) _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        var done = 0;
        var failed = 0;
        try
        {
            while (_waiting.TryDequeue(out var track))
            {
                if (await DownloadOneAsync(track).ConfigureAwait(false)) done++;
                else failed++;
                _pending.TryRemove(track.RatingKey, out _);
                _dispatcher.TryEnqueue(() =>
                {
                    Remaining = _pending.Count;
                    OnPropertyChanged(nameof(Count));
                    OnPropertyChanged(nameof(TotalBytes));
                    Changed?.Invoke(this, EventArgs.Empty);
                });
            }
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            if (!_waiting.IsEmpty && Interlocked.Exchange(ref _running, 1) == 0) _ = Task.Run(RunAsync);
        }
        await SaveAsync().ConfigureAwait(false);
        _dispatcher.TryEnqueue(() =>
        {
            if (failed > 0) _notifications.ShowError("Some songs didn't download", failed == 1 ? "1 song couldn't be downloaded." : $"{failed} songs couldn't be downloaded.");
            else if (done > 0 && _pending.IsEmpty) _notifications.ShowSuccess("Downloaded", done == 1 ? "1 song is ready to play offline." : $"{done} songs are ready to play offline.");
        });
    }

    private async Task<bool> DownloadOneAsync(Track track)
    {
        if (_session.Client is not { } client) return false;
        try
        {
            if (track.Media?.PartKey is null && await client.GetTrackAsync(track.RatingKey).ConfigureAwait(false) is { } full) track = full;
            var transcode = track.Media?.Codec is { } codec && PlayerService.IsTranscodeOnly(codec) || track.Media?.PartKey is null;
            var uri = transcode
                ? client.BuildTranscodeUri(track, 320, "download-" + Guid.NewGuid().ToString("N"))
                : client.BuildDirectPlayUri(track);
            if (uri is null) return false;
            var extension = transcode ? ".mp3" : Extension(track);
            var name = track.RatingKey + extension;
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, name);
            var temp = path + ".part";
            using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var target = File.Create(temp);
                await source.CopyToAsync(target).ConfigureAwait(false);
            }
            File.Move(temp, path, overwrite: true);
            _items[track.RatingKey] = new DownloadedTrack
            {
                Track = track,
                FileName = name,
                Bytes = new FileInfo(path).Length,
                ServerId = _session.Server?.MachineIdentifier,
                DownloadedAt = DateTimeOffset.UtcNow,
            };
            _log.LogInformation("Downloaded {Track}", track.Title);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or PlexException)
        {
            _log.LogWarning("Couldn't download {Track}: {Message}", track.Title, ex.Message);
            return false;
        }
    }

    private static string Extension(Track track)
    {
        var fromPart = Path.GetExtension(track.Media?.PartKey?.Split('?')[0] ?? string.Empty);
        if (fromPart is { Length: > 1 and <= 6 }) return fromPart.ToLowerInvariant();
        return "." + (track.Media?.Container ?? track.Media?.Codec ?? "audio").ToLowerInvariant();
    }

    public async Task RemoveAsync(IEnumerable<string> ratingKeys)
    {
        foreach (var key in ratingKeys)
        {
            if (!_items.TryRemove(key, out var item)) continue;
            try
            {
                File.Delete(Path.Combine(_folder, item.FileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogDebug("Download of {Track} couldn't be removed yet: {Message}", item.Track.Title, ex.Message);
            }
        }
        await SaveAsync().ConfigureAwait(false);
        _dispatcher.TryEnqueue(() =>
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(TotalBytes));
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public Task RemoveAllAsync() => RemoveAsync(_items.Keys.ToList());

    private void Load()
    {
        try
        {
            if (!File.Exists(IndexPath)) return;
            var items = JsonSerializer.Deserialize<List<DownloadedTrack>>(File.ReadAllText(IndexPath), Options) ?? [];
            foreach (var item in items.Where(i => File.Exists(Path.Combine(_folder, i.FileName))))
                _items[item.Track.RatingKey] = item;
            var known = _items.Values.Select(i => i.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(_folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("index.json", StringComparison.OrdinalIgnoreCase) || known.Contains(name)) continue;
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.LogWarning(ex, "Downloads list couldn't be read");
        }
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_folder);
            var json = JsonSerializer.Serialize(_items.Values.ToList(), Options);
            var temp = IndexPath + ".tmp";
            await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
            File.Move(temp, IndexPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.LogWarning(ex, "Downloads list couldn't be saved");
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
