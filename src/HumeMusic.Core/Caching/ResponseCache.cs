using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HumeMusic.Core.Caching;

/// <summary>
/// Cache for Plex GET responses. Identical concurrent requests share one in-flight task, and
/// completed results are reused for a time-to-live. Failed requests are never cached.
/// <para>
/// With a folder, answers are also kept on disk, so the app starts and opens pages from what it
/// already knows instead of asking the server for everything again: a kept answer is shown at once
/// and, once older than its time-to-live, quietly fetched again behind it (stale while revalidate).
/// After an edit, or when the library changes, kept answers must be checked with the server before
/// they're used again (<see cref="Invalidate"/>, <see cref="RequireRevalidation"/>), though they still
/// stand in when the server can't be reached.
/// </para>
/// </summary>
public sealed class ResponseCache
{
    private sealed record Entry(Task<object> Task, DateTimeOffset Created, TimeSpan Ttl);

    private sealed class Stored
    {
        public long SavedAt { get; set; }
        public int Generation { get; set; }
        public JsonElement Value { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, byte> _refreshing = new();
    private readonly SemaphoreSlim _refreshGate = new(3);
    private readonly Func<DateTimeOffset> _clock;
    private readonly string? _directory;
    private int _generation;
    private const int MaxEntries = 600;
    /// <summary>The most kept on disk; the oldest answers go first beyond it.</summary>
    private const long MaxDiskBytes = 300L * 1024 * 1024;

    public ResponseCache(Func<DateTimeOffset>? clock = null, string? directory = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _directory = directory;
        if (_directory is null) return;
        try
        {
            Directory.CreateDirectory(_directory);
            _generation = int.TryParse(ReadText(GenerationPath), out var g) ? g : 0;
            Trim(_directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _directory = null;
        }
    }

    private string GenerationPath => Path.Combine(_directory!, "generation.txt");

    private static readonly AsyncLocal<bool> Fresh = new();

    /// <summary>
    /// Within the returned scope, answers are fetched from the server (and kept) rather than taken
    /// from what's kept: for bringing one view up to date without forgetting everything else.
    /// </summary>
    public static IDisposable FetchFresh()
    {
        var before = Fresh.Value;
        Fresh.Value = true;
        return new Scope(() => Fresh.Value = before);
    }

    private sealed class Scope(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    public async Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory) where T : class
    {
        var now = _clock();
        if (Fresh.Value)
        {
            var value = await factory().ConfigureAwait(false);
            _entries[key] = new Entry(Task.FromResult<object>(value), now, ttl);
            Write(key, value);
            return value;
        }
        if (_entries.TryGetValue(key, out var existing))
        {
            if (!existing.Task.IsFaulted && !existing.Task.IsCanceled)
            {
                // An answer grown old is still shown at once; a newer one is fetched behind it.
                if (now - existing.Created >= existing.Ttl && existing.Task.IsCompletedSuccessfully)
                    _ = RefreshAsync(key, ttl, factory);
                return (T)await existing.Task.ConfigureAwait(false);
            }
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, existing));
        }

        // Kept from before: shown at once, and fetched again behind it when it has grown old. The file
        // is read off the caller's thread (often the UI's).
        var kept = _directory is null ? null : await Task.Run(() => Read<T>(key)).ConfigureAwait(false);
        if (kept is { Current: true } k)
        {
            _entries[key] = new Entry(Task.FromResult<object>(k.Value), k.SavedAt, ttl);
            if (now - k.SavedAt >= ttl) _ = RefreshAsync(key, ttl, factory);
            return k.Value;
        }

        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new Entry(tcs.Task, now, ttl);
        var winner = _entries.GetOrAdd(key, entry);
        if (!ReferenceEquals(winner, entry))
            return (T)await winner.Task.ConfigureAwait(false);

        if (_entries.Count > MaxEntries) Trim();

        try
        {
            var value = await factory().ConfigureAwait(false);
            tcs.SetResult(value);
            Write(key, value);
            return value;
        }
        catch (Exception ex)
        {
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            // The server couldn't answer: what was kept (however old) is better than nothing.
            if (kept is { } old && ex is not OperationCanceledException)
            {
                tcs.SetResult(old.Value);
                return old.Value;
            }
            tcs.SetException(ex);
            // Observe the exception on the shared task so waiters get it but it is never unobserved.
            _ = tcs.Task.Exception;
            throw;
        }
    }

    private async Task RefreshAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory) where T : class
    {
        if (!_refreshing.TryAdd(key, 0)) return;
        // A few at a time, so catching up never crowds out what's being waited for.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var value = await factory().ConfigureAwait(false);
            _entries[key] = new Entry(Task.FromResult<object>(value), _clock(), ttl);
            Write(key, value);
        }
        catch
        {
            // The kept answer stands until the server can be asked again.
        }
        finally
        {
            _refreshGate.Release();
            _refreshing.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Forgets answers (after an edit): from memory, and on disk they must be checked with the server
    /// before they're used again.
    /// </summary>
    public void Invalidate(Func<string, bool> predicate)
    {
        foreach (var key in _entries.Keys)
        {
            if (predicate(key)) _entries.TryRemove(key, out _);
        }
        RequireRevalidation();
    }

    /// <summary>Answers in memory are let go; those on disk stay, refreshed behind as they're used.</summary>
    public void MarkStale() => _entries.Clear();

    /// <summary>Every answer kept on disk must be checked with the server before it's used again.</summary>
    public void RequireRevalidation()
    {
        if (_directory is null) return;
        var next = Interlocked.Increment(ref _generation);
        try
        {
            File.WriteAllText(GenerationPath, next.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
        }
    }

    public void Clear()
    {
        _entries.Clear();
        if (_directory is null) return;
        foreach (var file in SafeFiles(_directory, "*.json"))
        {
            try { File.Delete(file); }
            catch (IOException) { }
        }
    }

    public int Count => _entries.Count;

    /// <summary>How much is kept on disk.</summary>
    public long DiskBytes => _directory is null ? 0 : SafeFiles(_directory, "*.json").Sum(f => new FileInfo(f).Length);

    // ------------------------------------------------------------------ disk

    private string FileFor(string key) =>
        Path.Combine(_directory!, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".json");

    private (T Value, DateTimeOffset SavedAt, bool Current)? Read<T>(string key) where T : class
    {
        if (_directory is null) return null;
        try
        {
            var path = FileFor(key);
            if (!File.Exists(path)) return null;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json);
            if (stored is null) return null;
            var value = stored.Value.Deserialize<T>(Plex.Json.PlexJson.Options);
            return value is null ? null : (value, DateTimeOffset.FromUnixTimeMilliseconds(stored.SavedAt), stored.Generation == Volatile.Read(ref _generation));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private void Write<T>(string key, T value)
    {
        if (_directory is null) return;
        var generation = Volatile.Read(ref _generation);
        var saved = _clock().ToUnixTimeMilliseconds();
        _ = Task.Run(() =>
        {
            try
            {
                var path = FileFor(key);
                var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new Stored { SavedAt = saved, Generation = generation, Value = JsonSerializer.SerializeToElement(value, Plex.Json.PlexJson.Options) }, Json));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Not kept this time; it's fetched again next time.
            }
        });
    }

    private static void Trim(string directory)
    {
        foreach (var temp in SafeFiles(directory, "*.tmp"))
        {
            try { File.Delete(temp); }
            catch (IOException) { }
        }
        var files = SafeFiles(directory, "*.json").Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
            if (total <= MaxDiskBytes) continue;
            try { file.Delete(); }
            catch (IOException) { }
        }
    }

    private static IEnumerable<string> SafeFiles(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Trim()
    {
        var now = _clock();
        foreach (var (key, e) in _entries)
        {
            if (now - e.Created >= e.Ttl) _entries.TryRemove(key, out _);
        }
        if (_entries.Count <= MaxEntries) return;
        foreach (var (key, _) in _entries.OrderBy(kv => kv.Value.Created).Take(_entries.Count - MaxEntries / 2))
            _entries.TryRemove(key, out _);
    }
}
