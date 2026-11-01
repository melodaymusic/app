using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MelodayMusic.Services;

public sealed record EngineProfile(Track Track, string Scope, string Revision, float[] Vector,
    double[] SampleOffsets, double SampledSeconds, double SampleRmsDb, double SamplePeakDb, DateTimeOffset AnalysedAt);

public sealed class MelodayEngine : IDisposable
{
    public const string Revision = "clap-music-9533ed82-three-windows-v1";
    private const string ModelHash = "9533ED823C150FB13C91ECD7359AA3BFF1AA6DA7B44099A21CCEA10C0ADBA2D6";
    private readonly PlexSession _session;
    private readonly ILogger<MelodayEngine> _log;
    private readonly ConcurrentDictionary<string, EngineProfile> _profiles = new();
    private readonly string _folder = Path.Combine(AppPaths.Cache, "engine");
    private InferenceSession? _model;
    private readonly object _modelGate = new();
    private DateTimeOffset _modelUsed;
    private int _borrowed;
    private bool _verified;
    private readonly Timer _modelIdle;
    private static readonly TimeSpan ModelIdle = TimeSpan.FromMinutes(2);
    private readonly Lazy<SoundVocabulary?> _vocabulary;
    private readonly SemaphoreSlim _calibrating = new(1, 1);
    private (string Scope, int Count, SoundCalibration Calibration)? _calibration;
    public event EventHandler? Changed;
    public string? Error { get; private set; }
    public string Scope => $"{_session.Server?.MachineIdentifier}|{_session.Section?.Key}";
    public int Count
    {
        get
        {
            var scope = Scope;
            return _profiles.Values.Count(p => p.Scope == scope);
        }
    }

    public bool Knows(Track track) => _profiles.ContainsKey(Key(Scope, track));

    public MelodayEngine(PlexSession session, ILogger<MelodayEngine> log)
    {
        _modelIdle = new Timer(_ => ReleaseIdleModel(), null, ModelIdle, TimeSpan.FromMinutes(1));
        _session = session;
        _log = log;
        _vocabulary = new(LoadVocabulary);
    }

    public SoundVocabulary? Vocabulary => _vocabulary.Value;

    private SoundVocabulary? LoadVocabulary()
    {
        try
        {
            var vocabulary = SoundVocabulary.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Engine", "descriptors.json"));
            _log.LogInformation("ME loaded {Count} words in {Categories} categories ({Revision})",
                vocabulary.Terms.Count, vocabulary.Categories.Count, vocabulary.Revision);
            return vocabulary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException
            or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            _log.LogWarning(ex, "ME words unavailable: {Type}", ex.GetType().Name);
            return null;
        }
    }

    public async Task<SoundCalibration> CalibrationAsync(CancellationToken ct = default)
    {
        if (Vocabulary is not { } vocabulary) return SoundCalibration.None;
        var scope = Scope;
        bool Fresh(int count) => _calibration is { } c && c.Scope == scope && Math.Abs(c.Count - count) <= Math.Max(20, count / 20);
        var profiles = Snapshot();
        if (Fresh(profiles.Count)) return _calibration!.Value.Calibration;
        await _calibrating.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Fresh(profiles.Count)) return _calibration!.Value.Calibration;
            var vectors = profiles.Values.OrderBy(p => p.Track.RatingKey, StringComparer.Ordinal).Select(p => p.Vector).ToArray();
            var calibration = await Task.Run(() => vocabulary.Calibrate(vectors, ct: ct), ct).ConfigureAwait(false);
            _calibration = (scope, profiles.Count, calibration);
            return calibration;
        }
        finally { _calibrating.Release(); }
    }

    public async Task<SoundDescription?> DescribeAsync(Track track, CancellationToken ct = default)
    {
        if (Vocabulary is not { } vocabulary || await CachedAsync(track, ct).ConfigureAwait(false) is not { } profile) return null;
        return vocabulary.Describe(profile.Vector, await CalibrationAsync(ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyDictionary<string, SoundDescription>> DescribeKnownAsync(IEnumerable<Track> tracks, CancellationToken ct = default)
    {
        if (Vocabulary is not { } vocabulary) return new Dictionary<string, SoundDescription>();
        var profiles = Snapshot();
        var known = tracks.Select(t => profiles.GetValueOrDefault(t.RatingKey)).OfType<EngineProfile>().DistinctBy(p => p.Track.RatingKey).ToArray();
        if (known.Length == 0) return new Dictionary<string, SoundDescription>();
        var calibration = await CalibrationAsync(ct).ConfigureAwait(false);
        return await Task.Run(() => known.ToDictionary(p => p.Track.RatingKey, p => vocabulary.Describe(p.Vector, calibration), StringComparer.Ordinal), ct)
            .ConfigureAwait(false);
    }

    public async Task<(SoundQuery? Query, IReadOnlyList<Track> Tracks)> SearchAsync(string text, int limit = 12, CancellationToken ct = default)
    {
        if (Vocabulary is not { } vocabulary) return (null, []);
        var query = vocabulary.Query(text);
        if (query.IsEmpty || query.Unknown.Count > 0) return (query, []);
        var profiles = Snapshot();
        if (profiles.Count == 0) return (query, []);
        var calibration = await CalibrationAsync(ct).ConfigureAwait(false);
        var ranked = await Task.Run(() => vocabulary.Search(query, profiles.Values.Select(p => (p.Track, p.Vector)), calibration, limit, ct), ct)
            .ConfigureAwait(false);
        return (query, ranked.Where(r => r.Score > 0).Select(r => r.Key).ToArray());
    }

    private static string Key(string scope, Track track) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{scope}|{track.RatingKey}|{track.DurationMs}|{Revision}")));

    public async Task<EngineProfile?> CachedAsync(Track track, CancellationToken ct = default)
    {
        var scope = Scope;
        var key = Key(scope, track);
        if (_profiles.TryGetValue(key, out var found)) return found;
        try
        {
            var path = Path.Combine(_folder, key + ".json");
            if (!File.Exists(path)) return null;
            var kept = JsonSerializer.Deserialize<EngineProfile>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
            if (kept is null || kept.Scope != scope || kept.Revision != Revision || kept.Track is null || kept.Track.RatingKey != track.RatingKey
                || kept.Track.DurationMs != track.DurationMs || kept.Vector is not { Length: 512 }
                || MusicSimilarity.Normalize(kept.Vector).Length != 512) return null;
            _profiles[key] = kept with { Track = track };
            return _profiles[key];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    // Audio is read outside the lock; the shared model runs concurrently and is released only when unused.
    public EngineProfile? Analyse(Track track, string scope, Func<double, float[]> read, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var latest = Math.Max(0, track.Duration.TotalSeconds - 10);
        var offsets = new[] { latest * 0.15, latest * 0.5, latest * 0.85 }.Distinct().ToArray();
        var windows = new List<float[]>();
        double squares = 0, peak = 0, seconds = 0;
        long count = 0;
        foreach (var offset in offsets)
        {
            ct.ThrowIfCancellationRequested();
            var samples = read(offset);
            if (samples.Length < 24000) return null;
            if (!samples.Any(s => float.IsFinite(s) && Math.Abs(s) > 1e-7f)
                || samples.Any(s => !float.IsFinite(s))) return null;
            var window = new float[480000];
            for (var i = 0; i < samples.Length; i++)
            {
                squares += samples[i] * (double)samples[i]; peak = Math.Max(peak, Math.Abs(samples[i]));
            }
            count += samples.Length; seconds += samples.Length / 48000.0;
            for (var i = 0; i < window.Length; i++)
                window[i] = (short)(Math.Clamp(samples[i % samples.Length], -1, 1) * 32767) / 32767f;
            windows.Add(window);
        }
        var model = Borrow();
        try
        {
            var vectors = new List<float[]>();
            foreach (var window in windows)
            {
                ct.ThrowIfCancellationRequested();
                var tensor = new DenseTensor<float>(window, [1, 480000]);
                using var output = model.Run([NamedOnnxValue.CreateFromTensor("waveform", tensor)]);
                var vector = MusicSimilarity.Normalize(output.First().AsTensor<float>().ToArray());
                if (vector.Length != 512) throw new InvalidDataException("ME model returned an invalid profile");
                vectors.Add(vector);
            }
            ct.ThrowIfCancellationRequested();
            var mean = MusicSimilarity.Normalize(Enumerable.Range(0, 512).Select(i => vectors.Average(v => v[i])));
            if (mean.Length != 512) return null;
            Error = null;
            return new(track, scope, Revision, mean, offsets, seconds,
                20 * Math.Log10(Math.Max(1e-9, Math.Sqrt(squares / Math.Max(1, count)))),
                20 * Math.Log10(Math.Max(1e-9, peak)), DateTimeOffset.UtcNow);
        }
        finally
        {
            lock (_modelGate)
            {
                _borrowed--;
                _modelUsed = DateTimeOffset.UtcNow;
            }
        }
    }

    private InferenceSession Borrow()
    {
        lock (_modelGate)
        {
            _model ??= LoadModel();
            _borrowed++;
            _modelUsed = DateTimeOffset.UtcNow;
            return _model;
        }
    }

    private InferenceSession LoadModel()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Engine", "audio.onnx");
        if (!_verified)
        {
            using var stream = File.OpenRead(path);
            if (Convert.ToHexString(SHA256.HashData(stream)) != ModelHash) throw new InvalidDataException("ME model integrity check failed");
            _verified = true;
        }
        OrtEnv.Instance().DisableTelemetryEvents();
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        var model = new InferenceSession(path, options);
        _log.LogInformation("ME loaded pinned CLAP music model; each song it analyses uses one CPU worker");
        return model;
    }

    public async Task KeepAsync(EngineProfile profile, CancellationToken ct)
    {
        var key = Key(profile.Scope, profile.Track);
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, key + ".json");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(profile), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        File.Move(temp, path, overwrite: true);
        _profiles[key] = profile;
        _log.LogInformation("ME profiled a track: {Samples} windows, {Seconds:0}s, 512 dimensions", profile.SampleOffsets.Length, profile.SampledSeconds);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Failed(Exception ex)
    {
        Error = "ME analysis is temporarily unavailable; retrying automatically";
        _log.LogWarning(ex, "ME analysis deferred: {Type}", ex.GetType().Name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyDictionary<string, EngineProfile> Snapshot() => _profiles.Values.Where(p => p.Scope == Scope)
        .GroupBy(p => p.Track.RatingKey).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.AnalysedAt).First());

    public void Reconcile(IReadOnlySet<string> tracks, string scope)
    {
        foreach (var entry in _profiles.Where(p => p.Value.Scope != scope || !tracks.Contains(p.Value.Track.RatingKey)).ToArray())
            _profiles.TryRemove(entry.Key, out _);
    }

    private void ReleaseIdleModel()
    {
        lock (_modelGate)
        {
            if (_model is null || _borrowed > 0 || DateTimeOffset.UtcNow - _modelUsed < ModelIdle) return;
            _model.Dispose();
            _model = null;
            _log.LogInformation("ME let its model go while idle");
        }
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
    }

    public void Dispose()
    {
        _modelIdle.Dispose();
        lock (_modelGate)
        {
            _model?.Dispose();
            _model = null;
        }
        _calibrating.Dispose();
    }
}
