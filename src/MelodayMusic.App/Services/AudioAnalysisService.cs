using System.Collections.Concurrent;
using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class AudioAnalysisService
{
    private static readonly TimeSpan HeadLength = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan TailLength = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new();

    private readonly PlexSession _session;
    private readonly DownloadService _downloads;
    private readonly ILogger<AudioAnalysisService> _log;
    private readonly MelodayEngine _engine;
    private readonly string _folder = Path.Combine(AppPaths.Cache, "analysis");
    private readonly ConcurrentDictionary<string, Task<AudioAnalysis?>> _results = new();
    public static readonly int MaxWidth = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    private readonly SemaphoreSlim _slots = new(1, MaxWidth);
    private readonly object _widthGate = new();
    private int _width = 1;

    public int Width => _width;

    // Narrowing takes effect as running analyses finish.
    public void SetWidth(int width)
    {
        width = Math.Clamp(width, 1, MaxWidth);
        lock (_widthGate)
        {
            for (; _width < width; _width++) _slots.Release();
            for (; _width > width; _width--) _ = _slots.WaitAsync();
        }
    }
    private readonly object _resultGate = new();
    private readonly ConcurrentQueue<Track> _priority = new();
    public event EventHandler? Available;
    public event Action<Track, bool, AudioAnalysis>? Completed;

    public void RequestPriority(IEnumerable<Track> tracks)
    {
        foreach (var track in tracks.Take(25)) _priority.Enqueue(track);
        while (_priority.Count > 50) _priority.TryDequeue(out _);
    }

    public void ClearPriority() { while (_priority.TryDequeue(out _)) { } }
    public bool HasPriority => !_priority.IsEmpty;
    public bool TryPriority(out Track? track) => _priority.TryDequeue(out track);

    public Task<AudioAnalysis?> WarmAsync(Track track, bool head, CancellationToken ct, bool fast = false) => Get(track, head, ct, background: !fast);

    public AudioAnalysisService(PlexSession session, DownloadService downloads, MelodayEngine engine, ILogger<AudioAnalysisService> log)
    {
        _session = session;
        _downloads = downloads;
        _log = log;
        _engine = engine;
    }

    public async Task<EngineProfile?> WarmEngineAsync(Track track, CancellationToken ct, bool fast = false)
    {
        if (await _engine.CachedAsync(track, ct).ConfigureAwait(false) is { } cached) return cached;
        var client = _session.Client;
        var scope = _engine.Scope;
        await _slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await _engine.CachedAsync(track, ct).ConfigureAwait(false) is { } ready) return ready;
            var full = track.Media?.PartKey is null && client is not null
                ? await client.GetTrackAsync(track.RatingKey, ct).ConfigureAwait(false) ?? track : track;
            var profile = await Task.Run(() =>
            {
                var thread = Thread.CurrentThread;
                var priority = thread.Priority;
                try
                {
                    thread.Priority = fast ? ThreadPriority.Normal : ThreadPriority.BelowNormal;
                    return _engine.Analyse(track, scope, offset =>
                    {
                        return Read(full, TimeSpan.FromSeconds(offset), TimeSpan.FromSeconds(10), client, ct, 48000);
                    }, ct);
                }
                finally { thread.Priority = priority; }
            }, ct).ConfigureAwait(false);
            if (profile is not null) await _engine.KeepAsync(profile, ct).ConfigureAwait(false);
            return profile;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _engine.Failed(ex); return null; }
        finally { _slots.Release(); }
    }

    private (string Source, TimeSpan Offset) Source(Track track, TimeSpan from, PlexServerClient? client)
    {
        if (_downloads.LocalUri(track) is { } local) return (local.LocalPath, from);
        if (client is null) throw new IOException("Library unavailable");
        if (track.Media?.Codec is { } codec && PlayerService.IsTranscodeOnly(codec))
            return (client.BuildTranscodeUri(track, 320, "analysis-" + Guid.NewGuid().ToString("N"), (long)from.TotalMilliseconds).ToString(), TimeSpan.Zero);
        if (client.BuildDirectPlayUri(track) is { } direct) return (direct.ToString(), from);
        throw new IOException("Track has no playable media");
    }

    private float[] Read(Track track, TimeSpan from, TimeSpan length, PlexServerClient? client, CancellationToken ct, int rate)
    {
        var source = Source(track, from, client);
        try { return AudioDecoder.ReadWindow(source.Source, source.Offset, length, ct, rate); }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0xC00D36C4) && client is not null)
        {
            ct.ThrowIfCancellationRequested();
            var transcoded = client.BuildTranscodeUri(track, 320, "analysis-" + Guid.NewGuid().ToString("N"), (long)from.TotalMilliseconds);
            var samples = AudioDecoder.ReadWindow(transcoded.ToString(), TimeSpan.Zero, length, ct, rate);
            _log.LogInformation("Audio analysis used an MP3 fallback for an unsupported container");
            return samples;
        }
    }

    public Task<AudioAnalysis?> HeadAsync(Track track) => Get(track, head: true);

    public Task<AudioAnalysis?> TailAsync(Track track) => Get(track, head: false);

    public Task<AudioAnalysis?> CachedHeadAsync(Track track) => CachedAsync(track, head: true);

    public Task<AudioAnalysis?> CachedTailAsync(Track track) => CachedAsync(track, head: false);

    private async Task<AudioAnalysis?> CachedAsync(Track track, bool head)
    {
        var key = Key(track, head);
        if (_results.TryGetValue(key, out var active) && active.IsCompletedSuccessfully) return active.Result;
        try
        {
            var path = PathFor(key);
            if (File.Exists(path) && JsonSerializer.Deserialize<Kept>(await File.ReadAllTextAsync(path).ConfigureAwait(false), Json)
                is { Version: AnalysisVersion, Analysis: { } kept }) return kept;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    private Task<AudioAnalysis?> Get(Track track, bool head, CancellationToken ct = default, bool background = false)
    {
        var key = Key(track, head);
        lock (_resultGate)
        {
            if (_results.TryGetValue(key, out var active) && (!active.IsCompleted || active.IsCompletedSuccessfully && active.Result is not null)) return active;
            var result = LoadAsync(track, head, key, _session.Client, ct, background);
            _results[key] = result;
            if (_results.Count > 512)
                foreach (var old in _results.Where(p => p.Key != key && p.Value.IsCompleted).Take(_results.Count - 512))
                    _results.TryRemove(old.Key, out _);
            return result;
        }
    }

    private string Key(Track track, bool head)
    {
        var scope = $"{_session.Server?.MachineIdentifier}|{_session.Section?.Key}|{track.RatingKey}|{track.DurationMs}|{(head ? "head" : "tail")}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scope)));
    }
    private string PathFor(string key) => Path.Combine(_folder, key.Replace('|', '-') + ".json");

    private sealed class Kept
    {
        public int Version { get; set; }
        public AudioAnalysis? Analysis { get; set; }
    }

    private const int AnalysisVersion = 1;

    private async Task<AudioAnalysis?> LoadAsync(Track track, bool head, string key, PlexServerClient? client, CancellationToken ct, bool background)
    {
        var path = PathFor(key);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Kept>(await File.ReadAllTextAsync(path), Json) is { Version: AnalysisVersion, Analysis: { } kept })
                return kept;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        try { await _slots.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        try
        {
            if (ct.IsCancellationRequested) return null;
            var analysis = await Task.Run(() =>
            {
                var thread = Thread.CurrentThread;
                var priority = thread.Priority;
                try
                {
                    if (background) thread.Priority = ThreadPriority.BelowNormal;
                    return Analyse(track, head, client, ct);
                }
                finally { if (background) thread.Priority = priority; }
            }).ConfigureAwait(false);
            if (analysis is null)
            {
                _results.TryRemove(key, out _);
                return null;
            }
            try
            {
                Directory.CreateDirectory(_folder);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Kept { Version = AnalysisVersion, Analysis = analysis }, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            _log.LogInformation("Listened to the {Part} of {Track}: {Bpm:0.0} bpm ({Confidence:0.00}), {Key}",
                head ? "start" : "end", track.Title, analysis.Bpm, analysis.TempoConfidence, AudioAnalysis.KeyName(analysis.Key));
            Available?.Invoke(this, EventArgs.Empty);
            if (Key(track, head) == key) Completed?.Invoke(track, head, analysis);
            return analysis;
        }
        finally
        {
            _slots.Release();
        }
    }

    private AudioAnalysis? Analyse(Track track, bool head, PlexServerClient? client, CancellationToken ct)
    {
        try
        {
            var duration = track.Duration;
            var from = head ? TimeSpan.Zero : duration - TailLength;
            if (from < TimeSpan.Zero) from = TimeSpan.Zero;
            var length = head ? HeadLength : TailLength;
            var full = track.Media?.PartKey is null && client is not null
                ? client.GetTrackAsync(track.RatingKey, ct).GetAwaiter().GetResult() ?? track : track;
            var samples = Read(full, from, length, client, ct, AudioDecoder.AnalysisRate);
            ct.ThrowIfCancellationRequested();
            return BeatAnalyzer.Analyse(samples, AudioDecoder.AnalysisRate, from.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogDebug("Couldn't listen to {Track}: {Message}", track.Title, ex.Message);
            return null;
        }
    }
}
